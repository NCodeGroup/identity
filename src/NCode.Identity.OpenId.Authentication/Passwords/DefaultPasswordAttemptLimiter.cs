// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Microsoft.Extensions.Logging;
using NCode.Identity.OpenId.Authentication.Logging;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.Authentication.Passwords;

/// <summary>
/// Provides the default <see cref="IPasswordAttemptLimiter"/>. It counts the failed attempts of an account in the
/// shared <see cref="IExpiringCounterSet"/>, keyed by the tenant, the connection and the folded account key. The first
/// failure starts a window of <c>password_failed_attempts_window</c>; once <c>password_failed_attempts_limit</c>
/// failures are counted, every password is refused until the window ends. A counter store that throws propagates the
/// exception, so the limit fails closed.
/// </summary>
// See ADR-1237.
internal class DefaultPasswordAttemptLimiter(
    IExpiringCounterSet counterSet,
    TimeProvider timeProvider,
    ILogger<DefaultPasswordAttemptLimiter> logger
) : IPasswordAttemptLimiter
{
    internal const string Purpose = "password-attempts";

    private IExpiringCounterSet CounterSet { get; } = counterSet;

    private TimeProvider TimeProvider { get; } = timeProvider;

    private ILogger<DefaultPasswordAttemptLimiter> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask<bool> IsLimitReachedAsync(
        OpenIdContext openIdContext,
        ResolvedConnection connection,
        string accountKey,
        CancellationToken cancellationToken
    )
    {
        var tenant = openIdContext.Tenant;
        var limit = connection.Settings.GetValue(OpenIdSettingKeys.PasswordFailedAttemptsLimit);

        var key = CreateKey(tenant.TenantId, connection.ConnectionId, accountKey);
        var count = await CounterSet.GetAsync(key, cancellationToken);
        if (count < limit)
            return false;

        Logger.PasswordAttemptLimitReached(tenant.TenantId, limit);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask RecordFailureAsync(
        OpenIdContext openIdContext,
        ResolvedConnection connection,
        string accountKey,
        CancellationToken cancellationToken
    )
    {
        var tenant = openIdContext.Tenant;
        var window = connection.Settings.GetValue(OpenIdSettingKeys.PasswordFailedAttemptsWindow);

        var key = CreateKey(tenant.TenantId, connection.ConnectionId, accountKey);
        var now = TimeProvider.GetUtcNow();
        var expiresWhen = now + window;
        await CounterSet.IncrementAsync(key, expiresWhen, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask ResetAsync(
        string tenantId,
        string connectionId,
        string accountKey,
        CancellationToken cancellationToken
    )
    {
        var key = CreateKey(tenantId, connectionId, accountKey);
        await CounterSet.RemoveAsync(key, cancellationToken);
    }

    internal static string CreateKey(string tenantId, string connectionId, string accountKey)
    {
        var normalizedKey = NormalizeAccountKey(accountKey);
        return ExpiringKeys.Create(Purpose, tenantId, connectionId, normalizedKey);
    }

    // The stores compare names differently (EF lower-cases; ASP.NET Core Identity composes and upper-cases), and
    // folding through both gives every spelling a store treats as the same name one count. The strings here come from
    // form or JSON readers, which never yield a lone surrogate, so normalization cannot fail.
    private static string NormalizeAccountKey(string accountKey) =>
        accountKey.Normalize(NormalizationForm.FormKC).ToUpperInvariant().ToLowerInvariant();
}
