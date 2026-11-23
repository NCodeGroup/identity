// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Buffers;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Authentication.Sessions;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides the local-account implementation of the <see cref="IInteractivePasswordResetter"/> seam, backed by the
/// configured <see cref="ILocalAccountStore"/>. It mails a one-time code only to the verified address of an enabled
/// account whose tenant allows resets, and it hands back a flow handle in every other case, so the answer never tells
/// whether an account exists. A reset judges the new password with the password policy before it spends the code,
/// replaces the password in one unit of work, ends the sessions of the account's principal and clears its failed
/// password attempts. It audits each request for a code and each attempt to reset.
/// </summary>
// See ADR-1533.
internal sealed class LocalAccountInteractivePasswordResetter(
    IStoreManagerFactory storeManagerFactory,
    IPasswordPolicy passwordPolicy,
    ILocalAccountEmailCodes emailCodes,
    ICryptoService cryptoService,
    IPostCommitSecurityEffects postCommitSecurityEffects,
    IAuditEventRecorder auditEventRecorder
) : IInteractivePasswordResetter
{
    /// <summary>
    /// The audit error code of a reset request that names no account a code may be sent to: none is found, it is
    /// disabled, or its address is missing or unverified.
    /// </summary>
    private const string AccountNotEligible = "account_not_eligible";

    /// <summary>
    /// Determines whether an account may be sent a password reset code, which holds only for an enabled account whose
    /// email address is verified.
    /// </summary>
    /// <param name="account">The account the end user named, or <see langword="null"/> when there is none.</param>
    /// <returns><see langword="true"/> when the account may be sent a code.</returns>
    private static bool IsEligible([NotNullWhen(true)] PersistedLocalAccount? account) =>
        account is { IsEnabled: true, EmailVerified: true, Email: { Length: > 0 } };

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IPasswordPolicy PasswordPolicy { get; } = passwordPolicy;
    private ILocalAccountEmailCodes EmailCodes { get; } = emailCodes;
    private ICryptoService CryptoService { get; } = cryptoService;
    private IPostCommitSecurityEffects PostCommitSecurityEffects { get; } =
        postCommitSecurityEffects;
    private IAuditEventRecorder AuditEventRecorder { get; } = auditEventRecorder;

    /// <inheritdoc />
    public async ValueTask<string> SendResetCodeAsync(
        OpenIdContext openIdContext,
        string userName,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    )
    {
        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        if (!settings.GetValue(OpenIdSettingKeys.PasswordResetEnabled))
        {
            return CryptoService.GenerateUrlSafeKey();
        }

        var (account, principalId) = await ReadResetCandidateAsync(userName, cancellationToken);

        // The end user's request is audited as the password grant's authentication is (ADR-1533).
        if (!IsEligible(account))
        {
            var decoyFlowId = await EmailCodes.SendDecoyAsync(
                openIdContext,
                EmailCodePurposes.ResetPassword,
                userName,
                cancellationToken
            );
            await AuditEventRecorder.RecordPasswordResetRequestFailedAsync(
                openIdContext,
                AccountNotEligible,
                userName,
                cancellationToken
            );
            return decoyFlowId;
        }

        var flowId = await EmailCodes.SendAsync(
            openIdContext,
            EmailCodePurposes.ResetPassword,
            account,
            locale,
            cancellationToken
        );
        if (flowId is null)
        {
            // SendAsync counted the send that found the account over its limit, so the decoy only adds its flow: every
            // path of a reset request the tenant allows does one increment and one add, so the sixth request's timing
            // says nothing about the account. A tenant that has resets off does no remote work for any username. See
            // ADR-1533.
            var decoyFlowId = await EmailCodes.AddDecoyFlowAsync(
                openIdContext,
                EmailCodePurposes.ResetPassword,
                cancellationToken
            );
            await AuditEventRecorder.RecordPasswordResetRequestFailedAsync(
                openIdContext,
                InteractiveEmailCodeErrors.TooManyCodes,
                userName,
                cancellationToken
            );
            return decoyFlowId;
        }

        await AuditEventRecorder.RecordPasswordResetRequestedAsync(
            openIdContext,
            principalId,
            account.LocalAccountId,
            cancellationToken
        );
        return flowId;
    }

    /// <inheritdoc />
    public async ValueTask<InteractivePasswordChangeResult> ResetPasswordAsync(
        OpenIdContext openIdContext,
        string flowId,
        Secret<char> code,
        Secret<char> newPassword,
        CancellationToken cancellationToken
    )
    {
        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        if (!settings.GetValue(OpenIdSettingKeys.PasswordResetEnabled))
        {
            return InteractivePasswordChangeResult.NotSupported;
        }

        var flow = new EmailCodeFlow(EmailCodePurposes.ResetPassword, flowId);
        var match = await EmailCodes.VerifyAsync(openIdContext, flow, code, cancellationToken);
        if (match is null)
        {
            // The end user's reset is audited as the password grant's authentication is (ADR-1533).
            return await RefuseInvalidCodeAsync(
                openIdContext,
                subjectId: null,
                localAccountId: null,
                cancellationToken
            );
        }

        var localAccountId = match.LocalAccountId;

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var accountStore = storeManager.GetStore<ILocalAccountStore>();
        var account = await accountStore.GetByIdOrDefaultAsync(localAccountId, cancellationToken);

        // A code authorizes nothing once the account's address differs from the one it was mailed to. See ADR-1533.
        if (account is not { IsEnabled: true } || !match.IsFor(account))
        {
            return await RefuseInvalidCodeAsync(
                openIdContext,
                subjectId: null,
                localAccountId: null,
                cancellationToken
            );
        }

        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();
        var principal = await identityStore.GetByIssuerSubjectAsync(
            AccountConstants.SelfIssuer,
            localAccountId,
            cancellationToken
        );

        // The new password is judged before the code is spent, so a password the policy refuses leaves the flow for
        // another try. See ADR-1236.
        var newPasswordMemory = newPassword.RevealMemory();
        var policyContext = new PasswordPolicyContext
        {
            Settings = settings,
            UserName = account.UserName,
            Email = account.Email,
        };
        var policyResult = await PasswordPolicy.ValidateAsync(
            policyContext,
            newPasswordMemory,
            cancellationToken
        );
        if (policyResult.IsRejected)
        {
            await AuditEventRecorder.RecordPasswordResetFailedAsync(
                openIdContext,
                principal?.PrincipalId,
                localAccountId,
                policyResult.Error,
                cancellationToken
            );
            return InteractivePasswordChangeResult.Refused(policyResult.Error);
        }

        // The code is spent after every check has passed and before its effect, with one atomic take, so two requests
        // that present the right code cannot both reset the password. See ADR-1533.
        var consumed = await EmailCodes.ConsumeAsync(openIdContext, flow, cancellationToken);
        if (!consumed)
        {
            return await RefuseInvalidCodeAsync(
                openIdContext,
                principal?.PrincipalId,
                localAccountId,
                cancellationToken
            );
        }

        // Encode the password into a pinned, zeroed-on-return buffer so it never lingers on the GC heap.
        var newPasswordChars = newPassword.Reveal();
        using var newOwner = SecureEncoding.UTF8.RentBytes(
            newPasswordChars,
            isSensitive: true,
            out Memory<byte> newBuffer
        );

        await accountStore.SetPasswordAsync(localAccountId, newBuffer, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        // The change is committed, so each effect and the record run on their own bounded token and a failure is
        // logged. The record follows the effects. See ADR-1229, ADR-1237 and ADR-1351.
        if (principal is not null)
        {
            await PostCommitSecurityEffects.TerminateSessionsAsync(
                openIdContext,
                principal.PrincipalId,
                SessionTerminationReasons.PasswordReset
            );
        }

        await PostCommitSecurityEffects.ResetPasswordAttemptsAsync(
            openIdContext.Tenant.TenantId,
            account.UserName
        );

        await AuditEventRecorder.RecordPasswordResetAsync(
            openIdContext,
            principal?.PrincipalId,
            localAccountId
        );

        return InteractivePasswordChangeResult.Changed;
    }

    /// <summary>
    /// Records the failed reset and refuses it as an invalid code.
    /// </summary>
    /// <param name="openIdContext">The context of the request.</param>
    /// <param name="subjectId">The subject identifier of the account's principal, or <see langword="null"/>.</param>
    /// <param name="localAccountId">The identifier of the account, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The refusal that tells the end user the code is invalid.</returns>
    private async ValueTask<InteractivePasswordChangeResult> RefuseInvalidCodeAsync(
        OpenIdContext openIdContext,
        string? subjectId,
        string? localAccountId,
        CancellationToken cancellationToken
    )
    {
        await AuditEventRecorder.RecordPasswordResetFailedAsync(
            openIdContext,
            subjectId,
            localAccountId,
            InteractiveEmailCodeErrors.InvalidCode,
            cancellationToken
        );
        return InteractivePasswordChangeResult.Refused(InteractiveEmailCodeErrors.InvalidCode);
    }

    /// <summary>
    /// Reads the account a reset request names and its principal in a unit of work of its own, which ends before a code
    /// is mailed. The principal is read for every request, by a random identifier when no account was found, so the
    /// answer's timing says nothing about the account.
    /// </summary>
    /// <param name="userName">The user name the end user named.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// The account, or <see langword="null"/> when there is none, and the identifier of its principal when the account
    /// may be sent a code, otherwise <see langword="null"/>.
    /// </returns>
    private async ValueTask<(
        PersistedLocalAccount? Account,
        string? PrincipalId
    )> ReadResetCandidateAsync(string userName, CancellationToken cancellationToken)
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var accountStore = storeManager.GetStore<ILocalAccountStore>();
        var account = await accountStore.GetByUserNameOrDefaultAsync(userName, cancellationToken);

        // The principal is read for every request, by a random identifier when no account was found, so the
        // answer's timing says nothing about the account. See ADR-1533.
        var principalSubject = account?.LocalAccountId ?? CryptoService.GenerateUrlSafeKey();
        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();
        var principal = await identityStore.GetByIssuerSubjectAsync(
            AccountConstants.SelfIssuer,
            principalSubject,
            cancellationToken
        );

        return (account, IsEligible(account) ? principal?.PrincipalId : null);
    }
}
