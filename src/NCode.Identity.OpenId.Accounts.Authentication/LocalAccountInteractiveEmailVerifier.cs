// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides the local-account implementation of the <see cref="IInteractiveEmailVerifier"/> seam, backed by the
/// configured <see cref="ILocalAccountStore"/>. It resolves the signed-in end user's single enabled local account,
/// mails a one-time code to the account's address when the tenant allows verification, and marks the address verified
/// when the end user presents the code. A code that belongs to another account verifies nothing. It audits each attempt
/// to verify.
/// </summary>
// See ADR-1533.
internal sealed class LocalAccountInteractiveEmailVerifier(
    IStoreManagerFactory storeManagerFactory,
    ILocalAccountEmailCodes emailCodes,
    IAuditEventRecorder auditEventRecorder
) : IInteractiveEmailVerifier
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private ILocalAccountEmailCodes EmailCodes { get; } = emailCodes;
    private IAuditEventRecorder AuditEventRecorder { get; } = auditEventRecorder;

    /// <inheritdoc />
    public async ValueTask<InteractiveEmailStatus?> GetStatusAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var account = await LocalAccountSignedInAccounts.FindAsync(
            storeManager,
            openIdContext,
            principal,
            cancellationToken
        );
        if (account is not { IsEnabled: true, Email: { Length: > 0 } email })
        {
            return null;
        }

        return new InteractiveEmailStatus(email, account.EmailVerified);
    }

    /// <inheritdoc />
    public async ValueTask<InteractiveEmailCodeResult> SendVerificationCodeAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    )
    {
        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        if (!settings.GetValue(OpenIdSettingKeys.EmailVerificationEnabled))
        {
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.NotSupported);
        }

        PersistedLocalAccount? account;
        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            account = await LocalAccountSignedInAccounts.FindAsync(
                storeManager,
                openIdContext,
                principal,
                cancellationToken
            );
        }

        if (account is not { IsEnabled: true })
        {
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.NotSupported);
        }

        if (string.IsNullOrEmpty(account.Email))
        {
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.NoEmail);
        }

        if (account.EmailVerified)
        {
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.AlreadyVerified);
        }

        var flowId = await EmailCodes.SendAsync(
            openIdContext,
            EmailCodePurposes.VerifyEmail,
            account,
            locale,
            cancellationToken
        );
        return flowId is null
            ? InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.TooManyCodes)
            : InteractiveEmailCodeResult.Sent(flowId);
    }

    /// <inheritdoc />
    public async ValueTask<InteractiveEmailCodeResult> VerifyEmailAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        string flowId,
        string code,
        CancellationToken cancellationToken
    )
    {
        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        if (!settings.GetValue(OpenIdSettingKeys.EmailVerificationEnabled))
        {
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.NotSupported);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var account = await LocalAccountSignedInAccounts.FindAsync(
            storeManager,
            openIdContext,
            principal,
            cancellationToken
        );
        if (account is not { IsEnabled: true })
        {
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.NotSupported);
        }

        var match = await EmailCodes.VerifyAsync(
            openIdContext,
            EmailCodePurposes.VerifyEmail,
            flowId,
            code,
            cancellationToken
        );

        // The end user's verification is audited as the password grant's authentication is (ADR-1533).
        var subjectId = openIdContext.GetSubjectId(principal);
        ArgumentException.ThrowIfNullOrEmpty(subjectId);

        // The code is spent after every check has passed, once it is bound to the account, and before its effect, with
        // one atomic take. See ADR-1533.
        if (
            match is null
            || !match.IsFor(account)
            || !await EmailCodes.ConsumeAsync(
                openIdContext,
                EmailCodePurposes.VerifyEmail,
                flowId,
                cancellationToken
            )
        )
        {
            await AuditEventRecorder.RecordEmailVerificationFailedAsync(
                openIdContext,
                subjectId,
                account.LocalAccountId,
                InteractiveEmailCodeErrors.InvalidCode,
                cancellationToken
            );
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.InvalidCode);
        }

        account.EmailVerified = true;
        await storeManager.GetStore<ILocalAccountStore>().UpdateAsync(account, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        await AuditEventRecorder.RecordEmailVerifiedAsync(
            openIdContext,
            subjectId,
            account.LocalAccountId,
            cancellationToken
        );

        return InteractiveEmailCodeResult.Verified;
    }
}
