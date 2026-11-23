// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using NCode.Buffers;
using NCode.Identity.Cryptography;
using NCode.Identity.Mail;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Provides the default <see cref="ILocalAccountEmailCodes"/>. It keeps each flow's state in the shared
/// <see cref="IExpiringValueSet"/>, so any replica of the cell can verify a code another replica sent, and it counts
/// the codes sent to an account in the shared <see cref="IExpiringCounterSet"/>. A flow keeps the code only as a
/// SHA-256 hash of the handle and the code, and it also holds the account identifier and the address the code was
/// mailed to. A store that throws propagates the exception, so a flow fails closed.
/// </summary>
// See ADR-1533.
internal sealed class DefaultLocalAccountEmailCodes(
    IExpiringValueSet valueSet,
    IExpiringCounterSet counterSet,
    IMailSender mailSender,
    ICryptoService cryptoService,
    TimeProvider timeProvider
) : ILocalAccountEmailCodes
{
    /// <summary>
    /// The number of guesses after which a flow ends.
    /// </summary>
    internal const int MaxAttempts = 5;

    /// <summary>
    /// The purpose of the key of a flow's state.
    /// </summary>
    internal const string FlowKeyPurpose = "email-code";

    /// <summary>
    /// The purpose of the key of the counter of the codes sent to an account.
    /// </summary>
    internal const string SendsKeyPurpose = "email-code-sends";

    /// <summary>
    /// The purpose of the key of the counter of the decoy flows started for an identifier.
    /// </summary>
    internal const string DecoySendsKeyPurpose = "email-code-decoy-sends";

    private const string DuplicateFlowMessage = "An email code flow with the same handle exists.";

    private const int CodeDigits = 6;

    private static int CodeUpperBound { get; } = (int)Math.Pow(10, CodeDigits);

    private static TimeSpan SendsWindow { get; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets the lifetime of a decoy's counter and flow.
    /// </summary>
    /// <remarks>
    /// A decoy flow refuses every code, an expired flow refuses a code with the same <c>invalid_code</c>, and no
    /// response reads the decoy counter, so a minute is unobservable. It keeps a flood of distinct usernames from
    /// filling shared state for an hour.
    /// </remarks>
    // See ADR-1533.
    internal static TimeSpan DecoyLifetime { get; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Creates the key a flow's state is stored under, which names the tenant, the purpose and the flow.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant the flow belongs to.</param>
    /// <param name="purpose">The purpose of the code the flow mails.</param>
    /// <param name="flowId">The identifier of the flow.</param>
    /// <returns>The key of the flow.</returns>
    internal static string CreateFlowKey(string tenantId, string purpose, string flowId) =>
        ExpiringKeys.Create(FlowKeyPurpose, tenantId, purpose, flowId);

    /// <summary>
    /// Draws a code and hashes it together with the flow it belongs to. The six digits of the code live only in a stack
    /// buffer that is cleared before this method returns.
    /// </summary>
    /// <param name="flowId">The identifier of the flow the code belongs to.</param>
    /// <param name="code">The code that was drawn.</param>
    /// <returns>The SHA-256 hash of the flow and the code.</returns>
    private static EmailCodeHash CreateCode(string flowId, out int code)
    {
        code = RandomNumberGenerator.GetInt32(CodeUpperBound);

        Span<char> digits = stackalloc char[CodeDigits];
        try
        {
            var formatted = new EmailCodeText(code).TryFormat(
                digits,
                out var written,
                format: default,
                CultureInfo.InvariantCulture
            );
            if (!formatted || written != CodeDigits)
            {
                throw new InvalidOperationException("The code does not format as six digits.");
            }

            var codeHash = default(EmailCodeHash);
            HashCode(flowId, digits, codeHash);
            return codeHash;
        }
        finally
        {
            digits.Clear();
        }
    }

    /// <summary>
    /// Selects the subject and the body format of the mail of a code by its purpose.
    /// </summary>
    /// <param name="purpose">The purpose of the code, one of the <see cref="EmailCodePurposes"/>.</param>
    /// <param name="messages">The messages of the locale that word the mail.</param>
    /// <returns>The subject and the composite format of the body.</returns>
    private static (string Subject, string BodyFormat) SelectMail(
        string purpose,
        InteractiveMessages messages
    ) =>
        purpose switch
        {
            EmailCodePurposes.VerifyEmail => (
                messages.VerificationMailSubject,
                messages.VerificationMailBodyFormat
            ),
            EmailCodePurposes.ResetPassword => (
                messages.PasswordResetMailSubject,
                messages.PasswordResetMailBodyFormat
            ),
            EmailCodePurposes.SignIn => (messages.SignInMailSubject, messages.SignInMailBodyFormat),
            _ => throw new ArgumentOutOfRangeException(
                nameof(purpose),
                purpose,
                "The purpose is not one of the email code purposes."
            ),
        };

    /// <summary>
    /// Hashes a code together with the flow it belongs to, so the state of a flow holds the hash and never the code.
    /// </summary>
    /// <param name="flowId">The identifier of the flow the code belongs to.</param>
    /// <param name="code">The code.</param>
    /// <param name="destination">The buffer that receives the SHA-256 hash of the flow and the code.</param>
    internal static void HashCode(string flowId, ReadOnlySpan<char> code, Span<byte> destination)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        using var flowOwner = SecureEncoding.UTF8.RentBytes(
            flowId,
            isSensitive: false,
            out Span<byte> flowBytes
        );
        hash.AppendData(flowBytes);

        hash.AppendData(":"u8);

        using var codeOwner = SecureEncoding.UTF8.RentBytes(
            code,
            isSensitive: true,
            out Span<byte> codeBytes
        );
        hash.AppendData(codeBytes);

        hash.GetHashAndReset(destination);
    }

    /// <summary>
    /// Writes the state of a flow as JSON into a buffer.
    /// </summary>
    /// <param name="state">The state to write.</param>
    /// <param name="buffer">The buffer that receives the JSON.</param>
    private static void WriteState(EmailCodeState state, IBufferWriter<byte> buffer)
    {
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(writer, state, EmailCodeJsonContext.Default.EmailCodeState);
    }

    /// <summary>
    /// Hashes an identifier, lowercased, so a counter key never holds the identifier itself.
    /// </summary>
    /// <param name="identifier">The identifier to hash.</param>
    /// <returns>The hexadecimal SHA-256 hash of the lowercased identifier.</returns>
    private static string HashIdentifier(string identifier)
    {
        using var charsOwner = BufferFactory.Rent<char>(
            identifier.Length,
            isSensitive: false,
            out Span<char> lowered
        );
        var written = identifier.AsSpan().ToLowerInvariant(lowered);

        using var bytesOwner = SecureEncoding.UTF8.RentBytes(
            lowered[..written],
            isSensitive: false,
            out Span<byte> bytes
        );

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, digest);
        return Convert.ToHexString(digest);
    }

    private IExpiringValueSet ValueSet { get; } = valueSet;
    private IExpiringCounterSet CounterSet { get; } = counterSet;
    private IMailSender MailSender { get; } = mailSender;
    private ICryptoService CryptoService { get; } = cryptoService;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    public async ValueTask<string?> SendAsync(
        OpenIdContext openIdContext,
        string purpose,
        PersistedLocalAccount account,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(account.Email))
        {
            throw new ArgumentException("The account has no email address.", nameof(account));
        }

        var tenant = openIdContext.Tenant;
        var settings = tenant.SettingsProvider.Collection;
        var now = TimeProvider.GetUtcNow();

        var sendsPerHour = settings.GetValue(OpenIdSettingKeys.EmailCodeSendsPerHour);
        var sendsKey = ExpiringKeys.Create(
            SendsKeyPurpose,
            tenant.TenantId,
            purpose,
            account.LocalAccountId
        );
        var sends = await CounterSet.IncrementAsync(sendsKey, now + SendsWindow, cancellationToken);
        if (sends > sendsPerHour)
        {
            return null;
        }

        var lifetime = settings.GetValue(OpenIdSettingKeys.EmailCodeLifetime);
        var expiresWhen = now + lifetime;

        var flowId = CryptoService.GenerateUrlSafeKey();
        var codeHash = CreateCode(flowId, out var code);
        var state = new EmailCodeState(
            account.LocalAccountId,
            account.Email,
            codeHash,
            Attempts: 0,
            expiresWhen
        );
        var flowKey = CreateFlowKey(tenant.TenantId, purpose, flowId);
        using var stateBuffer = BufferFactory.CreateContiguousBufferWriter(isSensitive: true);
        WriteState(state, stateBuffer);
        var added = await ValueSet.TryAddAsync(
            flowKey,
            stateBuffer.WrittenMemory,
            expiresWhen,
            cancellationToken
        );
        if (!added)
        {
            throw new InvalidOperationException(DuplicateFlowMessage);
        }

        var displayName = settings.GetValue(InteractiveSettingKeys.BrandingDisplayName);
        var fromDisplayName = string.IsNullOrEmpty(displayName) ? null : displayName;
        var (subject, bodyFormat) = SelectMail(purpose, locale.Messages);
        var lifetimeMinutes = (int)Math.Ceiling(lifetime.TotalMinutes);
        var mail = new LocalAccountCodeMail(
            account.Email,
            fromDisplayName,
            subject,
            bodyFormat,
            locale.LanguageTag,
            code,
            lifetimeMinutes
        );
        await MailSender.SendAsync(mail, cancellationToken);

        return flowId;
    }

    /// <inheritdoc />
    public async ValueTask<string> SendDecoyAsync(
        OpenIdContext openIdContext,
        string purpose,
        string identifier,
        CancellationToken cancellationToken
    )
    {
        var tenant = openIdContext.Tenant;
        var now = TimeProvider.GetUtcNow();

        // The decoy does the remote work of a send, so a request that mails nothing takes as long as one that does,
        // and it counts under a key of its own, so it never touches a real account's counter. See ADR-1533.
        var identifierHash = HashIdentifier(identifier);
        var sendsKey = ExpiringKeys.Create(
            DecoySendsKeyPurpose,
            tenant.TenantId,
            purpose,
            identifierHash
        );
        var expiresWhen = now + DecoyLifetime;
        await CounterSet.IncrementAsync(sendsKey, expiresWhen, cancellationToken);

        return await AddDecoyFlowAsync(openIdContext, purpose, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<string> AddDecoyFlowAsync(
        OpenIdContext openIdContext,
        string purpose,
        CancellationToken cancellationToken
    )
    {
        var tenant = openIdContext.Tenant;
        var expiresWhen = TimeProvider.GetUtcNow() + DecoyLifetime;

        var flowId = CryptoService.GenerateUrlSafeKey();

        // The code hash is random bytes, so no code verifies.
        var codeHash = new EmailCodeHash();
        RandomNumberGenerator.Fill(codeHash);
        var state = new EmailCodeState(
            LocalAccountId: string.Empty,
            Email: string.Empty,
            codeHash,
            Attempts: 0,
            expiresWhen
        );
        var flowKey = CreateFlowKey(tenant.TenantId, purpose, flowId);
        using var stateBuffer = BufferFactory.CreateContiguousBufferWriter(isSensitive: true);
        WriteState(state, stateBuffer);
        var added = await ValueSet.TryAddAsync(
            flowKey,
            stateBuffer.WrittenMemory,
            expiresWhen,
            cancellationToken
        );
        if (!added)
        {
            throw new InvalidOperationException(DuplicateFlowMessage);
        }

        return flowId;
    }

    /// <inheritdoc />
    public async ValueTask<EmailCodeMatch?> VerifyAsync(
        OpenIdContext openIdContext,
        EmailCodeFlow flow,
        Secret<char> code,
        CancellationToken cancellationToken
    )
    {
        var key = CreateFlowKey(openIdContext.Tenant.TenantId, flow.Purpose, flow.FlowId);
        var presentedHash = default(EmailCodeHash);
        var presentedCode = code.Reveal();
        HashCode(flow.FlowId, presentedCode, presentedHash);

        // Each guess is counted before it is judged, so at most five guesses are compared against one flow however
        // many race, and a lost swap reads the flow again.
        while (true)
        {
            var stored = await ValueSet.GetAsync(key, cancellationToken);
            if (stored is not { } value)
            {
                return null;
            }

            EmailCodeState? state;
            try
            {
                state = JsonSerializer.Deserialize(
                    value.Value.Span,
                    EmailCodeJsonContext.Default.EmailCodeState
                );
            }
            catch (JsonException)
            {
                // A stored flow that lacks a member, or holds a null where none is written, is malformed, so it
                // refuses every code. See ADR-1533.
                return null;
            }

            if (state is null)
            {
                return null;
            }

            // A flow whose expiry has passed by this replica's clock refuses every code, so a refused replace below
            // only ever means that another guess changed the flow. See ADR-1533.
            var now = TimeProvider.GetUtcNow();
            if (state.ExpiresWhen <= now)
            {
                return null;
            }

            if (state.Attempts >= MaxAttempts)
            {
                await ValueSet.RemoveAsync(key, cancellationToken);
                return null;
            }

            var attempts = state.Attempts + 1;
            var updatedState = state with { Attempts = attempts };
            using var updatedBuffer = BufferFactory.CreateContiguousBufferWriter(isSensitive: true);
            WriteState(updatedState, updatedBuffer);
            var replaced = await ValueSet.TryReplaceAsync(
                key,
                value.Version,
                updatedBuffer.WrittenMemory,
                state.ExpiresWhen,
                cancellationToken
            );
            if (!replaced)
            {
                continue;
            }

            var storedHash = state.CodeHash;
            if (CryptographicOperations.FixedTimeEquals(storedHash, presentedHash))
            {
                return new EmailCodeMatch(state.LocalAccountId, state.Email);
            }

            if (attempts >= MaxAttempts)
            {
                await ValueSet.RemoveAsync(key, cancellationToken);
            }

            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> ConsumeAsync(
        OpenIdContext openIdContext,
        EmailCodeFlow flow,
        CancellationToken cancellationToken
    )
    {
        var key = CreateFlowKey(openIdContext.Tenant.TenantId, flow.Purpose, flow.FlowId);
        var taken = await ValueSet.TryTakeAsync(key, cancellationToken);
        return taken is not null;
    }
}
