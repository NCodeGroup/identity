// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides the local-account implementation of the <see cref="IInteractiveSignInCodeChallenge"/> seam, backed by the
/// configured <see cref="ILocalAccountStore"/>. It resolves the end user's single enabled local account from the
/// pending sign-in's principal, mails a one-time code to the account's address, verified or not, and completes the
/// second factor when the end user presents the code. A right code marks an unverified address verified, and a code
/// that belongs to another account completes nothing. It audits each attempt to complete the second factor and each
/// refused send.
/// </summary>
// See ADR-1533.
internal sealed class LocalAccountInteractiveSignInCodeChallenge(
    IStoreManagerFactory storeManagerFactory,
    ILocalAccountEmailCodes emailCodes,
    IAuditEventRecorder auditEventRecorder
) : IInteractiveSignInCodeChallenge
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private ILocalAccountEmailCodes EmailCodes { get; } = emailCodes;
    private IAuditEventRecorder AuditEventRecorder { get; } = auditEventRecorder;

    /// <inheritdoc />
    public async ValueTask<InteractiveEmailCodeResult> SendSignInCodeAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    )
    {
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
            await RecordSendRefusedAsync(
                openIdContext,
                principal,
                account,
                InteractiveEmailCodeErrors.NoEmail,
                cancellationToken
            );
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.NoEmail);
        }

        var flowId = await EmailCodes.SendAsync(
            openIdContext,
            EmailCodePurposes.SignIn,
            account,
            locale,
            cancellationToken
        );
        if (flowId is null)
        {
            await RecordSendRefusedAsync(
                openIdContext,
                principal,
                account,
                InteractiveEmailCodeErrors.TooManyCodes,
                cancellationToken
            );
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.TooManyCodes);
        }

        return InteractiveEmailCodeResult.Sent(flowId);
    }

    private async ValueTask RecordSendRefusedAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        PersistedLocalAccount account,
        string errorCode,
        CancellationToken cancellationToken
    )
    {
        // A refused send blocks a sign-in, so it is audited as the second factor's failure (ADR-1533).
        var subjectId = openIdContext.GetSubjectId(principal);
        ArgumentException.ThrowIfNullOrEmpty(subjectId);

        await AuditEventRecorder.RecordSecondFactorFailedAsync(
            openIdContext,
            subjectId,
            account.LocalAccountId,
            AuditCredentialFactors.EmailCode,
            errorCode,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask<InteractiveEmailCodeResult> VerifySignInCodeAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        string flowId,
        string code,
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
        if (account is not { IsEnabled: true })
        {
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.NotSupported);
        }

        var localAccountId = await EmailCodes.VerifyAsync(
            openIdContext,
            EmailCodePurposes.SignIn,
            flowId,
            code,
            cancellationToken
        );

        // The second factor is audited as the password grant's authentication is (ADR-1533).
        var subjectId = openIdContext.GetSubjectId(principal);
        ArgumentException.ThrowIfNullOrEmpty(subjectId);

        if (!string.Equals(localAccountId, account.LocalAccountId, StringComparison.Ordinal))
        {
            await AuditEventRecorder.RecordSecondFactorFailedAsync(
                openIdContext,
                subjectId,
                account.LocalAccountId,
                AuditCredentialFactors.EmailCode,
                InteractiveEmailCodeErrors.InvalidCode,
                cancellationToken
            );
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.InvalidCode);
        }

        // The code is spent after every check has passed and before its effect, with one atomic take. See ADR-1533.
        var consumed = await EmailCodes.ConsumeAsync(
            openIdContext,
            EmailCodePurposes.SignIn,
            flowId,
            cancellationToken
        );
        if (!consumed)
        {
            await AuditEventRecorder.RecordSecondFactorFailedAsync(
                openIdContext,
                subjectId,
                account.LocalAccountId,
                AuditCredentialFactors.EmailCode,
                InteractiveEmailCodeErrors.InvalidCode,
                cancellationToken
            );
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.InvalidCode);
        }

        if (!account.EmailVerified)
        {
            account.EmailVerified = true;
            await storeManager
                .GetStore<ILocalAccountStore>()
                .UpdateAsync(account, cancellationToken);
            await storeManager.SaveChangesAsync(cancellationToken);
        }

        await AuditEventRecorder.RecordSecondFactorVerifiedAsync(
            openIdContext,
            subjectId,
            account.LocalAccountId,
            AuditCredentialFactors.EmailCode,
            cancellationToken
        );

        return InteractiveEmailCodeResult.Verified;
    }
}
