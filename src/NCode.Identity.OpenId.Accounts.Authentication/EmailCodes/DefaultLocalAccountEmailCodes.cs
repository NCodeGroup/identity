// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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

    private static string CodeFormat { get; } =
        string.Create(CultureInfo.InvariantCulture, $"D{CodeDigits}");

    private static TimeSpan SendsWindow { get; } = TimeSpan.FromHours(1);

    // The lifetime of a decoy's counter and flow. A decoy flow refuses every code, an expired flow refuses a code with
    // the same invalid_code, and no response reads the decoy counter, so a minute is unobservable. It keeps a flood of
    // distinct usernames from filling shared state for an hour.
    internal static TimeSpan DecoyLifetime { get; } = TimeSpan.FromMinutes(1);

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
        var code = RandomNumberGenerator
            .GetInt32(CodeUpperBound)
            .ToString(CodeFormat, CultureInfo.InvariantCulture);

        var codeHash = HashCode(flowId, code);
        var state = new EmailCodeState(
            account.LocalAccountId,
            account.Email,
            codeHash,
            Attempts: 0,
            expiresWhen
        );
        var flowKey = CreateFlowKey(tenant.TenantId, purpose, flowId);
        var stateBytes = JsonSerializer.SerializeToUtf8Bytes(
            state,
            EmailCodeJsonContext.Default.EmailCodeState
        );
        var added = await ValueSet.TryAddAsync(flowKey, stateBytes, expiresWhen, cancellationToken);
        if (!added)
        {
            throw new InvalidOperationException(DuplicateFlowMessage);
        }

        var displayName = settings.GetValue(InteractiveSettingKeys.BrandingDisplayName);
        var fromDisplayName = string.IsNullOrEmpty(displayName) ? null : displayName;
        var message = LocalAccountMailComposer.Compose(
            purpose,
            account.Email,
            code,
            lifetime,
            locale,
            fromDisplayName
        );
        await MailSender.SendAsync(message, cancellationToken);

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
        var loweredIdentifier = identifier.ToLowerInvariant();
        var identifierBytes = SecureEncoding.UTF8.GetBytes(loweredIdentifier);
        var identifierDigest = SHA256.HashData(identifierBytes);
        var identifierHash = Convert.ToHexString(identifierDigest);
        var sendsKey = ExpiringKeys.Create(
            DecoySendsKeyPurpose,
            tenant.TenantId,
            purpose,
            identifierHash
        );
        var expiresWhen = now + DecoyLifetime;
        await CounterSet.IncrementAsync(sendsKey, expiresWhen, cancellationToken);

        var flowId = CryptoService.GenerateUrlSafeKey();

        // The code hash is that of random bytes, so no code verifies.
        using var randomOwner = BufferFactory.RentRandom(
            SHA256.HashSizeInBytes,
            isSensitive: false,
            out Span<byte> randomBytes
        );
        var codeHash = SHA256.HashData(randomBytes);
        var state = new EmailCodeState(
            identifierHash,
            Email: string.Empty,
            codeHash,
            Attempts: 0,
            expiresWhen
        );
        var flowKey = CreateFlowKey(tenant.TenantId, purpose, flowId);
        var stateBytes = JsonSerializer.SerializeToUtf8Bytes(
            state,
            EmailCodeJsonContext.Default.EmailCodeState
        );
        var added = await ValueSet.TryAddAsync(flowKey, stateBytes, expiresWhen, cancellationToken);
        if (!added)
        {
            throw new InvalidOperationException(DuplicateFlowMessage);
        }

        return flowId;
    }

    /// <inheritdoc />
    public async ValueTask<EmailCodeMatch?> VerifyAsync(
        OpenIdContext openIdContext,
        string purpose,
        string flowId,
        string code,
        CancellationToken cancellationToken
    )
    {
        var key = CreateFlowKey(openIdContext.Tenant.TenantId, purpose, flowId);
        var presentedHash = HashCode(flowId, code);

        // Each guess is counted before it is judged, so at most five guesses are compared against one flow however
        // many race, and a lost swap reads the flow again.
        while (true)
        {
            var stored = await ValueSet.GetAsync(key, cancellationToken);
            if (stored is not { } value)
            {
                return null;
            }

            var state = JsonSerializer.Deserialize(
                value.Value.Span,
                EmailCodeJsonContext.Default.EmailCodeState
            );
            if (state is null)
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
            var updatedBytes = JsonSerializer.SerializeToUtf8Bytes(
                updatedState,
                EmailCodeJsonContext.Default.EmailCodeState
            );
            var replaced = await ValueSet.TryReplaceAsync(
                key,
                value.Version,
                updatedBytes,
                state.ExpiresWhen,
                cancellationToken
            );
            if (!replaced)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(state.CodeHash, presentedHash))
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
        string purpose,
        string flowId,
        CancellationToken cancellationToken
    )
    {
        var key = CreateFlowKey(openIdContext.Tenant.TenantId, purpose, flowId);
        var taken = await ValueSet.TryTakeAsync(key, cancellationToken);
        return taken is not null;
    }

    internal static string CreateFlowKey(string tenantId, string purpose, string flowId) =>
        ExpiringKeys.Create(FlowKeyPurpose, tenantId, purpose, flowId);

    internal static byte[] HashCode(string flowId, string code)
    {
        using var owner = SecureEncoding.UTF8.RentBytes(
            $"{flowId}:{code}",
            isSensitive: true,
            out Memory<byte> buffer
        );
        return SHA256.HashData(buffer.Span);
    }
}
