// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Identity.Cryptography;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.PrincipalResolution;

/// <summary>
/// Provides the default implementation of <see cref="IPrincipalResolver"/>, which resolves an authenticated caller to
/// a federated principal: the subject claim is interpreted first as a server-owned <c>PrincipalId</c>, then as the
/// principal of a client acting for itself and, when neither matches, as the subject of the connection that the
/// server-owned connection claim names. A client acting for itself resolves to its principal while the client is
/// enabled and to none when it is disabled, and is never provisioned as a federated principal. Only a principal whose
/// connection claim names an enabled connection is looked up or provisioned. The subject is derived through the
/// environment's host-configured <c>GetSubjectId</c>.
/// </summary>
// See ADR-1053, ADR-1466 and ADR-1535.
internal class DefaultPrincipalResolver(
    IStoreManagerFactory storeManagerFactory,
    ICryptoService cryptoService,
    IFederatedIdentityLinkingPolicy linkingPolicy,
    IClientPrincipalResolver clientPrincipalResolver,
    IConnectionResolver connectionResolver
) : IPrincipalResolver
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private ICryptoService CryptoService { get; } = cryptoService;
    private IFederatedIdentityLinkingPolicy LinkingPolicy { get; } = linkingPolicy;
    private IClientPrincipalResolver ClientPrincipalResolver { get; } = clientPrincipalResolver;
    private IConnectionResolver ConnectionResolver { get; } = connectionResolver;

    private async ValueTask<ResolvedConnection?> ResolveEnabledConnectionAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken
    )
    {
        var connectionId = user.FindFirstValue(ConnectionClaimTypes.Connection);
        if (string.IsNullOrEmpty(connectionId))
        {
            return null;
        }

        var connection = await ConnectionResolver.GetByIdOrDefaultAsync(
            openIdContext,
            connectionId,
            cancellationToken
        );
        return connection is { Connection.IsEnabled: true } ? connection : null;
    }

    private async ValueTask<(
        bool IsActingClient,
        string? PrincipalId,
        ResolvedConnection? Connection
    )> FindPrincipalIdAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal user,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var subject = openIdContext.GetSubjectId(user);
        if (string.IsNullOrEmpty(subject))
        {
            return (false, null, null);
        }

        // The subject may already be a server-owned PrincipalId (the steady state, where the server issued the token).
        var principalStore = storeManager.GetStore<IFederatedPrincipalStore>();
        var existingPrincipal = await principalStore.GetOrDefaultAsync(subject, cancellationToken);
        if (existingPrincipal is not null)
        {
            return (false, existingPrincipal.PrincipalId, null);
        }

        // Otherwise the subject may be the principal of the client that is acting for itself.
        var actingClient = await ClientPrincipalResolver.GetActingClientOrDefaultAsync(
            openIdContext,
            user,
            storeManager,
            cancellationToken
        );
        if (actingClient is not null)
        {
            return (true, actingClient.IsDisabled ? null : actingClient.PrincipalId, null);
        }

        // Otherwise the subject is a value of the connection that the server-owned connection claim names.
        var connection = await ResolveEnabledConnectionAsync(
            openIdContext,
            user,
            cancellationToken
        );
        if (connection is null)
        {
            return (false, null, null);
        }

        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();
        var identity = await identityStore.GetByConnectionSubjectAsync(
            connection.ConnectionId,
            subject,
            cancellationToken
        );
        return (false, identity?.PrincipalId, connection);
    }

    /// <inheritdoc />
    public async ValueTask<string?> ResolvePrincipalIdOrDefaultAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal user,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var found = await FindPrincipalIdAsync(
            openIdContext,
            user,
            storeManager,
            cancellationToken
        );
        return found.PrincipalId;
    }

    /// <inheritdoc />
    public async ValueTask<string> ResolvePrincipalIdAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal user,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var found = await FindPrincipalIdAsync(
            openIdContext,
            user,
            storeManager,
            cancellationToken
        );
        if (found.PrincipalId is not null)
        {
            return found.PrincipalId;
        }

        // A client acting for itself is never provisioned as a federated principal.
        if (found.IsActingClient)
        {
            return string.Empty;
        }

        var subject = openIdContext.GetSubjectId(user);

        // Without a subject and an enabled connection we cannot form a durable identity for idempotent lookup, so the
        // subject value is used as-is: for a non-federated caller the subject is already the principal reference, and
        // provisioning on every sight would create duplicates (ADR-1035).
        var connection = found.Connection;
        if (string.IsNullOrEmpty(subject) || connection is null)
        {
            return subject ?? string.Empty;
        }

        var decision = await LinkingPolicy.ResolveLinkAsync(
            openIdContext,
            connection,
            user,
            storeManager,
            cancellationToken
        );

        var principalStore = storeManager.GetStore<IFederatedPrincipalStore>();
        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();

        string principalId;
        if (decision.LinkedPrincipalId is { } linkedPrincipalId)
        {
            principalId = linkedPrincipalId;
        }
        else
        {
            principalId = CryptoService.GenerateResourceId();
            var federatedPrincipal = new PersistedFederatedPrincipal
            {
                TenantId = openIdContext.Tenant.TenantId,
                PrincipalId = principalId,
                ConcurrencyToken = string.Empty,
            };
            await principalStore.AddAsync(federatedPrincipal, cancellationToken);
        }

        var issuer = user.FindFirstValue(JoseClaimNames.Payload.Iss);
        var federatedIdentity = new PersistedFederatedIdentity
        {
            TenantId = openIdContext.Tenant.TenantId,
            FederatedIdentityId = CryptoService.GenerateResourceId(),
            PrincipalId = principalId,
            ConnectionId = connection.ConnectionId,
            Issuer = issuer,
            Subject = subject,
            JoinKey = decision.JoinKey,
            ConcurrencyToken = string.Empty,
        };
        await identityStore.AddAsync(federatedIdentity, cancellationToken);

        return principalId;
    }

    /// <inheritdoc />
    public async ValueTask<string> ResolvePrincipalIdAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var principalId = await ResolvePrincipalIdAsync(
            openIdContext,
            user,
            storeManager,
            cancellationToken
        );
        await storeManager.SaveChangesAsync(cancellationToken);

        // A caller with no resolvable upstream identity has no provisioned principal; its raw subject is already the
        // reference (ADR-1035), so fall back to it rather than returning an empty subject.
        return string.IsNullOrEmpty(principalId)
            ? openIdContext.GetSubjectId(user) ?? string.Empty
            : principalId;
    }
}
