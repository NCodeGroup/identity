// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides the local-account implementation of the <see cref="IInteractiveSignInCodeChallenge"/> seam, backed by the
/// configured <see cref="ILocalAccountStore"/>. It resolves the end user's single enabled local account from the
/// pending sign-in's principal, mails a one-time code to the account's address, verified or not, and completes the
/// second factor when the end user presents the code. A right code marks an unverified address verified, and a code
/// that belongs to another account completes nothing.
/// </summary>
// See ADR-1533.
internal sealed class LocalAccountInteractiveSignInCodeChallenge(
    IStoreManagerFactory storeManagerFactory,
    ILocalAccountEmailCodes emailCodes
) : IInteractiveSignInCodeChallenge
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private ILocalAccountEmailCodes EmailCodes { get; } = emailCodes;

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
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.NoEmail);
        }

        var flowId = await EmailCodes.SendAsync(
            openIdContext,
            EmailCodePurposes.SignIn,
            account,
            locale,
            cancellationToken
        );
        return flowId is null
            ? InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.TooManyCodes)
            : InteractiveEmailCodeResult.Sent(flowId);
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
        if (!string.Equals(localAccountId, account.LocalAccountId, StringComparison.Ordinal))
        {
            return InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.InvalidCode);
        }

        await EmailCodes.CompleteAsync(
            openIdContext,
            EmailCodePurposes.SignIn,
            flowId,
            cancellationToken
        );

        if (!account.EmailVerified)
        {
            account.EmailVerified = true;
            await storeManager
                .GetStore<ILocalAccountStore>()
                .UpdateAsync(account, cancellationToken);
            await storeManager.SaveChangesAsync(cancellationToken);
        }

        return InteractiveEmailCodeResult.Verified;
    }
}
