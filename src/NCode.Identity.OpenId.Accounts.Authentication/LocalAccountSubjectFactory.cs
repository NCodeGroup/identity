// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Claims;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Settings;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Builds the subject of a local account that has just verified a password: the tenant's subject claim, the
/// self-issued issuer claim, the tenant identifier, the authentication time, the display name as <c>name</c>, the
/// account's own claims, the <c>pwd</c> authentication method and the resolved authentication context class reference.
/// The password grant and the interactive sign-in both build their subject with it, so the two paths stamp the same
/// claims.
/// </summary>
// See ADR-1035, ADR-1051, ADR-1055.
internal sealed class LocalAccountSubjectFactory(
    IAuthenticationContextReferenceResolver authenticationContextReferenceResolver,
    TimeProvider timeProvider
)
{
    private IAuthenticationContextReferenceResolver AuthenticationContextReferenceResolver { get; } =
        authenticationContextReferenceResolver;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <summary>
    /// Creates the principal of a local account whose password was just verified.
    /// </summary>
    /// <param name="openIdContext">The context that names the tenant the account signs in to.</param>
    /// <param name="account">The local account that verified its password.</param>
    /// <param name="authenticationType">The authentication type of the principal's identity.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The principal that carries the account's subject claims under <paramref name="authenticationType"/>.</returns>
    public async ValueTask<ClaimsPrincipal> CreateAsync(
        OpenIdContext openIdContext,
        PersistedLocalAccount account,
        string authenticationType,
        CancellationToken cancellationToken
    )
    {
        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        var subjectClaimType = settings.GetValue(OpenIdSettingKeys.SubjectClaimTypes).First();
        var issuerClaimType = settings.GetValue(OpenIdSettingKeys.PrincipalIssuerClaim);

        // The subject is built fresh with no interactive challenge to stash the tenant and authentication time in
        // auth properties, so both are stamped as claims for DefaultValidateSubjectAuthenticationHandler. The local
        // account verified a password, so the authentication time is now.
        var authTime = TimeProvider.GetUtcNow().ToUnixTimeSeconds();
        var authTimeValue = authTime.ToString(CultureInfo.InvariantCulture);
        var claims = new List<Claim>(account.Claims.Count + 7)
        {
            // The subject is stamped under the tenant's primary subject claim type so principal resolution reads it
            // back through the same tenant-aware GetSubjectId seam (ADR-1053).
            new(subjectClaimType, account.LocalAccountId),
            new(issuerClaimType, AccountConstants.SelfIssuer),
            new(JoseClaimNames.Payload.Tid, openIdContext.Tenant.TenantId),
            new(JoseClaimNames.Payload.AuthTime, authTimeValue, ClaimValueTypes.Integer64),
        };
        // The display name is the account's 'name' claim.
        var nameClaim = new Claim(JoseClaimNames.Payload.Name, account.DisplayName);
        claims.Add(nameClaim);
        var accountClaims = account.Claims.Select(claim => new Claim(claim.Type, claim.Value));
        claims.AddRange(accountClaims);

        // The local account verified a password, so record 'pwd' (RFC 8176) and resolve the authentication context
        // class reference (acr) from it, letting the deployment's policy assert a stronger value when applicable.
        var amrClaim = new Claim(
            JoseClaimNames.Payload.Amr,
            OpenIdProtocol.AuthenticationMethods.Password
        );
        claims.Add(amrClaim);

        var acrRequest = new AuthenticationContextReferenceRequest
        {
            AuthenticationMethods = [OpenIdProtocol.AuthenticationMethods.Password],
            IdentityProvider = null,
            Settings = settings,
        };
        var acr = await AuthenticationContextReferenceResolver.ResolveAsync(
            acrRequest,
            cancellationToken
        );
        if (!string.IsNullOrEmpty(acr))
        {
            var acrClaim = new Claim(JoseClaimNames.Payload.Acr, acr);
            claims.Add(acrClaim);
        }

        var identity = new ClaimsIdentity(claims, authenticationType);
        return new ClaimsPrincipal(identity);
    }
}
