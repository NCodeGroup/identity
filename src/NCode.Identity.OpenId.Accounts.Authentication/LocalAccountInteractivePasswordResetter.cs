// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Buffers;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
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
/// password attempts.
/// </summary>
// See ADR-1533.
internal sealed class LocalAccountInteractivePasswordResetter(
    IStoreManagerFactory storeManagerFactory,
    IPasswordPolicy passwordPolicy,
    ILocalAccountEmailCodes emailCodes,
    ICryptoService cryptoService,
    IPostCommitSecurityEffects postCommitSecurityEffects
) : IInteractivePasswordResetter
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IPasswordPolicy PasswordPolicy { get; } = passwordPolicy;
    private ILocalAccountEmailCodes EmailCodes { get; } = emailCodes;
    private ICryptoService CryptoService { get; } = cryptoService;
    private IPostCommitSecurityEffects PostCommitSecurityEffects { get; } =
        postCommitSecurityEffects;

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

        PersistedLocalAccount? account;
        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            account = await storeManager
                .GetStore<ILocalAccountStore>()
                .GetByUserNameOrDefaultAsync(userName, cancellationToken);
        }

        if (account is not { IsEnabled: true, EmailVerified: true, Email: { Length: > 0 } })
        {
            return CryptoService.GenerateUrlSafeKey();
        }

        var flowId = await EmailCodes.SendAsync(
            openIdContext,
            EmailCodePurposes.ResetPassword,
            account,
            locale,
            cancellationToken
        );
        return flowId ?? CryptoService.GenerateUrlSafeKey();
    }

    /// <inheritdoc />
    public async ValueTask<InteractivePasswordChangeResult> ResetPasswordAsync(
        OpenIdContext openIdContext,
        string flowId,
        string code,
        string newPassword,
        CancellationToken cancellationToken
    )
    {
        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        if (!settings.GetValue(OpenIdSettingKeys.PasswordResetEnabled))
        {
            return InteractivePasswordChangeResult.NotSupported;
        }

        var localAccountId = await EmailCodes.VerifyAsync(
            openIdContext,
            EmailCodePurposes.ResetPassword,
            flowId,
            code,
            cancellationToken
        );
        if (localAccountId is null)
        {
            return InteractivePasswordChangeResult.Refused(InteractiveEmailCodeErrors.InvalidCode);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var accountStore = storeManager.GetStore<ILocalAccountStore>();
        var account = await accountStore.GetByIdOrDefaultAsync(localAccountId, cancellationToken);
        if (account is not { IsEnabled: true })
        {
            return InteractivePasswordChangeResult.Refused(InteractiveEmailCodeErrors.InvalidCode);
        }

        // The new password is judged before the code is spent, so a password the policy refuses leaves the flow for
        // another try. See ADR-1236.
        var newPasswordMemory = newPassword.AsMemory();
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
            return InteractivePasswordChangeResult.Refused(policyResult.Error);
        }

        var principal = await storeManager
            .GetStore<IFederatedIdentityStore>()
            .GetByIssuerSubjectAsync(
                AccountConstants.SelfIssuer,
                localAccountId,
                cancellationToken
            );

        // Encode the password into a pinned, zeroed-on-return buffer so it never lingers on the GC heap.
        using var newOwner = SecureEncoding.UTF8.RentBytes(
            newPassword,
            isSensitive: true,
            out Memory<byte> newBuffer
        );

        await accountStore.SetPasswordAsync(localAccountId, newBuffer, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        await EmailCodes.CompleteAsync(
            openIdContext,
            EmailCodePurposes.ResetPassword,
            flowId,
            cancellationToken
        );

        // The change is committed, so each effect runs on its own bounded token and a failure is logged. See ADR-1229,
        // ADR-1237 and ADR-1351.
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

        return InteractivePasswordChangeResult.Changed;
    }
}
