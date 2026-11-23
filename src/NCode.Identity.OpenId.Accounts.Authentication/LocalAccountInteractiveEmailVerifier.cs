// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
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
            return InteractiveEmailCodeResult.NotSupported;
        }

        var account = await LocalAccountSignedInAccounts.FindAsync(
            StoreManagerFactory,
            openIdContext,
            principal,
            cancellationToken
        );

        if (account is not { IsEnabled: true })
        {
            return InteractiveEmailCodeResult.NotSupported;
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
        Secret<char> code,
        CancellationToken cancellationToken
    )
    {
        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        if (!settings.GetValue(OpenIdSettingKeys.EmailVerificationEnabled))
        {
            return InteractiveEmailCodeResult.NotSupported;
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
            return InteractiveEmailCodeResult.NotSupported;
        }

        // The end user's verification is audited as the password grant's authentication is (ADR-1533).
        var subjectId = openIdContext.GetSubjectId(principal);
        ArgumentException.ThrowIfNullOrEmpty(subjectId);

        // The code is spent after every check has passed, once it is bound to the account, and before its effect, with
        // one atomic take. See ADR-1533.
        var flow = new EmailCodeFlow(EmailCodePurposes.VerifyEmail, flowId);
        if (!await EmailCodes.TryRedeemAsync(openIdContext, flow, code, account, cancellationToken))
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

        var accountStore = storeManager.GetStore<ILocalAccountStore>();
        account.EmailVerified = true;
        await accountStore.UpdateAsync(account, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        await AuditEventRecorder.RecordEmailVerifiedAsync(
            openIdContext,
            subjectId,
            account.LocalAccountId
        );

        return InteractiveEmailCodeResult.Verified;
    }
}
