// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides the local-account implementation of the <see cref="IInteractiveCredentialVerifier"/> seam, backed by the
/// configured <see cref="ILocalAccountStore"/> — the same credential path the resource-owner password grant uses. It
/// verifies the submitted username/password and, on success, builds the cookie <see cref="ClaimsPrincipal"/> carrying
/// the subject, issuer, tenant, and authentication-time claims the authorize flow reads, and the <c>name</c> claim from
/// the account's display name. A host fronting a different backend (LDAP, an external identity provider, passwordless)
/// registers its own implementation instead. A password for an account that reached its limit of failed attempts is
/// verified against the store's dummy hash and refused, as a wrong one is.
/// </summary>
internal sealed class LocalAccountInteractiveCredentialVerifier(
    IStoreManagerFactory storeManagerFactory,
    LocalAccountSubjectFactory localAccountSubjectFactory,
    IPasswordAttemptLimiter passwordAttemptLimiter
) : IInteractiveCredentialVerifier
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private LocalAccountSubjectFactory LocalAccountSubjectFactory { get; } =
        localAccountSubjectFactory;
    private IPasswordAttemptLimiter PasswordAttemptLimiter { get; } = passwordAttemptLimiter;

    /// <inheritdoc />
    public async ValueTask<ClaimsPrincipal?> VerifyCredentialAsync(
        OpenIdContext openIdContext,
        string username,
        string password,
        CancellationToken cancellationToken
    )
    {
        var result = await VerifyCredentialsAsync(
            openIdContext,
            username,
            password,
            cancellationToken
        );

        // A password refused at the limit is verified against the store's dummy hash and answered as a wrong one, and
        // both count as a failure under the user name the store holds. A disabled account goes on to the sign-in's
        // standing check, which refuses it with its reason. See ADR-1229, ADR-1237.
        if (!result.IsVerified)
        {
            // A failure is recorded even when the client disconnects.
            await PasswordAttemptLimiter.RecordFailureAsync(
                openIdContext,
                result.UserName,
                CancellationToken.None
            );
            return null;
        }

        await PasswordAttemptLimiter.ResetAsync(
            openIdContext.Tenant.TenantId,
            result.UserName,
            cancellationToken
        );

        return await LocalAccountSubjectFactory.CreateAsync(
            openIdContext,
            result.Account,
            InteractiveAuthenticationDefaults.CookieSchemeName,
            cancellationToken
        );
    }

    private async ValueTask<LocalAccountVerificationResult> VerifyCredentialsAsync(
        OpenIdContext openIdContext,
        string username,
        string password,
        CancellationToken cancellationToken
    )
    {
        // Encode the password into a pinned, zeroed-on-return buffer so the credential never lingers on the GC heap.
        using var owner = SecureEncoding.UTF8.RentBytes(
            password,
            isSensitive: true,
            out Memory<byte> buffer
        );

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ILocalAccountStore>();

        // The store asks the gate once, with the user name it holds, between its lookup and its verification. The gate
        // refuses a result that a store verified after a refusal and fails a store that never asked, so no store lifts
        // the limit. See ADR-1237.
        var gate = new PasswordAttemptGate(PasswordAttemptLimiter, openIdContext);
        var storedResult = await store.VerifyCredentialAsync(
            username,
            buffer,
            gate.IsRefusedAsync,
            cancellationToken
        );
        var result = gate.Enforce(storedResult);

        // Persist a transparent rehash-on-verify, when one occurred, within this unit of work.
        if (result.IsVerified)
        {
            await storeManager.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
