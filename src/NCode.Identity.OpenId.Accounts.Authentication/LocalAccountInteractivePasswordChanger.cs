// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides the local-account implementation of the <see cref="IInteractivePasswordChanger"/> seam, backed by the
/// configured <see cref="ILocalAccountStore"/>. It resolves the signed-in end user's single enabled local account,
/// verifies the current password through the same credential path the interactive sign-in uses, and replaces the
/// password in one unit of work. It judges the new password with the password policy before it verifies the current
/// one, and verifies the current password under the limit on failed attempts. An end user without exactly one enabled
/// local account has no password to change here. It audits each change, and each refusal of the new or the current
/// password, as a <c>password.change</c> event that carries no password.
/// </summary>
// See ADR-1230.
internal sealed class LocalAccountInteractivePasswordChanger(
    IStoreManagerFactory storeManagerFactory,
    LocalAccountSignedInAccountFinder signedInAccountFinder,
    LocalAccountStoreCatalog localAccountStoreCatalog,
    IPasswordPolicy passwordPolicy,
    IPasswordAttemptLimiter passwordAttemptLimiter,
    IAuditEventRecorder auditEventRecorder
) : IInteractivePasswordChanger
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private LocalAccountSignedInAccountFinder SignedInAccountFinder { get; } =
        signedInAccountFinder;
    private LocalAccountStoreCatalog LocalAccountStoreCatalog { get; } = localAccountStoreCatalog;
    private IPasswordPolicy PasswordPolicy { get; } = passwordPolicy;
    private IPasswordAttemptLimiter PasswordAttemptLimiter { get; } = passwordAttemptLimiter;
    private IAuditEventRecorder AuditEventRecorder { get; } = auditEventRecorder;

    /// <inheritdoc />
    public async ValueTask<InteractivePasswordChangeResult> ChangePasswordAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        Secret<char> currentPassword,
        Secret<char> newPassword,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        // A principal that links several local accounts has no single password to change.
        var found = await SignedInAccountFinder.FindAsync(
            storeManager,
            openIdContext,
            principal,
            cancellationToken
        );
        if (found is not { Account: { IsEnabled: true } account, Connection: var connection })
        {
            return InteractivePasswordChangeResult.NotSupported;
        }

        // The end user's credential change is audited as the password grant's authentication is (ADR-1533).
        var principalId = openIdContext.GetSubjectId(principal);
        ArgumentException.ThrowIfNullOrEmpty(principalId);

        // The new password is judged before the current one is verified, so a password the policy refuses costs no
        // verification. See ADR-1236.
        var newPasswordMemory = newPassword.RevealMemory();
        var policyContext = new PasswordPolicyContext
        {
            Settings = connection.Settings,
            UserName = account.UserName ?? string.Empty,
            Email = account.Email,
        };
        var policyResult = await PasswordPolicy.ValidateAsync(
            policyContext,
            newPasswordMemory,
            cancellationToken
        );
        if (policyResult.IsRejected)
        {
            await AuditEventRecorder.RecordPasswordChangeFailedAsync(
                openIdContext,
                principalId,
                account.LocalAccountId,
                policyResult.Error,
                cancellationToken
            );
            return InteractivePasswordChangeResult.Refused(policyResult.Error);
        }

        // Encode each password into its own pinned, zeroed-on-return buffer so the credentials never linger on the GC
        // heap.
        var currentPasswordChars = currentPassword.Reveal();
        using var currentOwner = SecureEncoding.UTF8.RentBytes(
            currentPasswordChars,
            isSensitive: true,
            out Memory<byte> currentBuffer
        );

        // The store asks the gate once, with the account key it holds, between its lookup and its verification, and a
        // password refused at the limit is verified against its dummy hash and counts as a failure, as a wrong one
        // does. The gate refuses a result that a store verified after a refusal and fails a store that never asked, so
        // no store lifts the limit. See ADR-1237.
        var accountStore = LocalAccountStoreCatalog.GetStore(storeManager, connection.Connection);
        var identifier = new LocalAccountSignInIdentifier
        {
            ConnectionId = connection.ConnectionId,
            UserName = account.UserName,
            Email = account.UserName is null ? account.Email : null,
        };
        var gate = new PasswordAttemptGate(PasswordAttemptLimiter, openIdContext, connection);
        var storedResult = await accountStore.VerifyCredentialAsync(
            identifier,
            currentBuffer,
            gate.IsRefusedAsync,
            cancellationToken
        );
        var result = gate.Enforce(storedResult);
        if (
            !result.IsVerified
            || !string.Equals(
                result.Account.LocalAccountId,
                account.LocalAccountId,
                StringComparison.Ordinal
            )
        )
        {
            // A failure is recorded even when the client disconnects.
            await PasswordAttemptLimiter.RecordFailureAsync(
                openIdContext,
                connection,
                result.AccountKey,
                CancellationToken.None
            );
            await AuditEventRecorder.RecordPasswordChangeFailedAsync(
                openIdContext,
                principalId,
                account.LocalAccountId,
                InteractivePasswordChangeErrors.InvalidCurrentPassword,
                CancellationToken.None
            );
            return InteractivePasswordChangeResult.CurrentPasswordInvalid;
        }

        await PasswordAttemptLimiter.ResetAsync(
            openIdContext.Tenant.TenantId,
            connection.ConnectionId,
            result.AccountKey,
            cancellationToken
        );

        var newPasswordChars = newPassword.Reveal();
        using var newOwner = SecureEncoding.UTF8.RentBytes(
            newPasswordChars,
            isSensitive: true,
            out Memory<byte> newBuffer
        );

        // The verification's transparent rehash, when one occurred, commits with the new password.
        await accountStore.SetPasswordAsync(account.LocalAccountId, newBuffer, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        await AuditEventRecorder.RecordPasswordChangedAsync(
            openIdContext,
            principalId,
            account.LocalAccountId
        );

        return InteractivePasswordChangeResult.Changed;
    }
}
