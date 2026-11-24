// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Endpoints.Authorization.Messages;
using NCode.Identity.OpenId.Authentication.Endpoints.UserInfo.Commands;
using NCode.Identity.OpenId.Authentication.Persistence;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides a <see cref="GetUserInfoClaimsCommand"/> handler that returns the authenticated subject's
/// <c>profile</c>/<c>email</c>/<c>address</c>/<c>phone</c> scope claims from the configured
/// <see cref="ILocalAccountStore"/>. Per OpenID Connect Core 1.0 §5.4 these scope-driven claims are returned from the
/// UserInfo endpoint (not the ID token) whenever an access token was issued, so the matching gate in the ID-token
/// subject-claims handler keeps them out of the ID token and this handler supplies them here. In addition, individual
/// claims requested via the OIDC <c>claims</c> request parameter's <c>userinfo</c> member (OIDC Core §5.5) are served
/// from the account store even when the owning scope was not granted. The local account is resolved from the caller's
/// principal via its identity in a <c>local</c> connection, and only the claims permitted by the token's granted scopes
/// or explicitly requested are emitted. A host fronting a different user store registers its own enricher instead. The
/// <c>name</c> claim comes from the account's display name.
/// </summary>
// See ADR-1401, ADR-1051.
internal sealed class DefaultGetLocalAccountUserInfoClaimsHandler(
    IStoreManagerFactory storeManagerFactory,
    IPersistedGrantService persistedGrantService,
    IConnectionResolver connectionResolver,
    LocalAccountStoreCatalog catalog
) : ICommandHandler<GetUserInfoClaimsCommand>, ISupportMediatorPriority
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IPersistedGrantService PersistedGrantService { get; } = persistedGrantService;
    private IConnectionResolver ConnectionResolver { get; } = connectionResolver;
    private LocalAccountStoreCatalog Catalog { get; } = catalog;

    /// <inheritdoc />
    public int MediatorPriority => DefaultMediatorPriorities.Low;

    /// <inheritdoc />
    public async ValueTask HandleAsync(
        GetUserInfoClaimsCommand command,
        CancellationToken cancellationToken
    )
    {
        var (openIdContext, subjectAuthentication, claims) = command;

        var scopes = GetGrantedScopes(subjectAuthentication.Subject);
        var wantsEmail = scopes.Contains(OpenIdProtocol.ScopeTypes.Email);
        var claimTypes = CollectScopeClaimTypes(scopes);

        // Also honor individual claims requested via the OIDC 'claims' parameter's 'userinfo' member (OIDC Core §5.5),
        // which are served from the account store regardless of scope; 'value'/'values' are advisory and 'essential' is
        // best-effort, so a requested claim the account does not carry is simply omitted.
        var requestedClaimTypes = await GetRequestedUserInfoClaimTypesAsync(
            openIdContext,
            subjectAuthentication.Subject,
            cancellationToken
        );
        claimTypes.UnionWith(requestedClaimTypes);

        if (!wantsEmail && claimTypes.Count == 0)
        {
            return;
        }

        var account = await ResolveLocalAccountAsync(
            openIdContext,
            subjectAuthentication.PrincipalId,
            subjectAuthentication.Subject,
            cancellationToken
        );
        if (account is null)
        {
            return;
        }

        // The email address and its verification status are first-class account fields, not profile claims, so the
        // email scope projects them directly (email_verified as a JSON boolean, which the suite checks by type).
        if (wantsEmail && !string.IsNullOrEmpty(account.Email))
        {
            TryAdd(claims, JoseClaimNames.Payload.Email, account.Email);
            TryAdd(claims, JoseClaimNames.Payload.EmailVerified, account.EmailVerified);
        }

        // The display name is the account's 'name' claim, a first-class account field rather than a profile claim.
        if (claimTypes.Contains(JoseClaimNames.Payload.Name))
        {
            TryAdd(claims, JoseClaimNames.Payload.Name, account.DisplayName);
        }

        if (claimTypes.Count == 0)
        {
            return;
        }

        foreach (var claim in account.Claims)
        {
            if (claimTypes.Contains(claim.Type))
            {
                var claimValue = ToClaimValue(claim.Type, claim.Value);
                TryAdd(claims, claim.Type, claimValue);
            }
        }
    }

    private async ValueTask<IReadOnlySet<string>> GetRequestedUserInfoClaimTypesAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal subject,
        CancellationToken cancellationToken
    )
    {
        // The requested 'claims' were persisted at access-token issuance as a grant keyed by the token's jti.
        var jti = subject.FindFirstValue(JoseClaimNames.Payload.Jti);
        if (string.IsNullOrEmpty(jti))
        {
            return FrozenSet<string>.Empty;
        }

        var grantId = PersistedGrantService.CreateGrantId(
            openIdContext.Tenant.TenantId,
            OpenIdConstants.PersistedGrantTypes.UserInfoClaims,
            jti
        );

        var grant = await PersistedGrantService.GetOrDefaultAsync<IRequestClaims>(
            openIdContext,
            grantId,
            cancellationToken
        );

        var requested = grant?.Payload.UserInfo;
        if (requested is null || requested.Count == 0)
        {
            return FrozenSet<string>.Empty;
        }

        return requested.Keys.ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlySet<string> GetGrantedScopes(ClaimsPrincipal subject)
    {
        // RFC 9068 §2.2.3 carries the granted scopes on the access token as a single space-delimited 'scope' claim.
        var scope = subject.FindFirst(JoseClaimNames.Payload.Scope)?.Value;
        if (string.IsNullOrEmpty(scope))
        {
            return FrozenSet<string>.Empty;
        }

        return scope
            .Split(
                OpenIdProtocol.ParameterSeparatorChar,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> CollectScopeClaimTypes(IReadOnlySet<string> scopes)
    {
        var claimTypes = new HashSet<string>(StringComparer.Ordinal);

        if (scopes.Contains(OpenIdProtocol.ScopeTypes.Profile))
        {
            claimTypes.UnionWith(OpenIdConstants.ClaimsByScope.Profile);
        }

        if (scopes.Contains(OpenIdProtocol.ScopeTypes.Address))
        {
            claimTypes.UnionWith(OpenIdConstants.ClaimsByScope.Address);
        }

        if (scopes.Contains(OpenIdProtocol.ScopeTypes.Phone))
        {
            claimTypes.UnionWith(OpenIdConstants.ClaimsByScope.Phone);
        }

        return claimTypes;
    }

    private async ValueTask<PersistedLocalAccount?> ResolveLocalAccountAsync(
        OpenIdContext openIdContext,
        string principalId,
        ClaimsPrincipal subject,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        // The principal owns an identity in a local connection whose subject is the local account id (ADR-1051); the
        // caller presenting a non-local (external-IdP) principal simply resolves no local account and is skipped.
        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();
        var identities = await identityStore.GetByPrincipalAsync(principalId, cancellationToken);

        // A principal can hold local identities in several connections, so the account is the one in the connection
        // the subject signed in through; a subject without that claim names an account only when there is one.
        var signedInConnectionId = subject.FindFirstValue(ConnectionClaimTypes.Connection);
        var localIdentities =
            new List<(PersistedFederatedIdentity Identity, ResolvedConnection Connection)>();
        foreach (var identity in identities)
        {
            var connection = await ConnectionResolver.GetByIdOrDefaultAsync(
                openIdContext,
                identity.ConnectionId,
                cancellationToken
            );
            if (connection is null || !LocalAccountSignedInAccounts.IsLocal(connection))
            {
                continue;
            }

            if (string.IsNullOrEmpty(signedInConnectionId))
            {
                localIdentities.Add((identity, connection));
                continue;
            }

            if (
                string.Equals(identity.ConnectionId, signedInConnectionId, StringComparison.Ordinal)
            )
            {
                var accountStore = Catalog.GetStore(storeManager, connection.Connection);
                return await accountStore.GetByIdOrDefaultAsync(
                    identity.Subject,
                    cancellationToken
                );
            }
        }

        if (localIdentities.Count != 1)
        {
            return null;
        }

        var (localIdentity, localConnection) = localIdentities[0];
        var localStore = Catalog.GetStore(storeManager, localConnection.Connection);
        return await localStore.GetByIdOrDefaultAsync(localIdentity.Subject, cancellationToken);
    }

    private static void TryAdd(IDictionary<string, object> claims, string claimType, object value)
    {
        // Do not override a claim another enricher already supplied (for example the required 'sub').
        claims.TryAdd(claimType, value);
    }

    private static object ToClaimValue(string claimType, string rawValue)
    {
        // Account claims persist as strings, but several standard OIDC claims have non-string JSON types the UserInfo
        // response must honor (OIDC Core §5.1 / §5.1.1); coerce the well-known ones and leave everything else a string.
        switch (claimType)
        {
            case JoseClaimNames.Payload.UpdatedAt
                when long.TryParse(
                    rawValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var seconds
                ):
                return seconds;

            case JoseClaimNames.Payload.PhoneNumberVerified
                when bool.TryParse(rawValue, out var verified):
                return verified;

            case JoseClaimNames.Payload.Address:
                // The address claim is a structured JSON object; parse it so it serializes as an object, not a string.
                try
                {
                    return JsonNode.Parse(rawValue) ?? rawValue;
                }
                catch (JsonException)
                {
                    return rawValue;
                }

            default:
                return rawValue;
        }
    }
}
