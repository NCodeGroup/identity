// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Stores;

/// <summary>
/// Provides a default implementation of <see cref="IFederatedIdentityStore"/> that uses Entity Framework Core for
/// persistence.
/// </summary>
internal class FederatedIdentityStore(
    IStoreProvider storeProvider,
    ISurrogateKeyGenerator keyGenerator,
    IPageCursorProtector cursorProtector,
    OpenIdDbContext openIdDbContext
) : CoreBaseStore<PersistedFederatedIdentity, FederatedIdentityEntity>, IFederatedIdentityStore
{
    /// <inheritdoc />
    protected override IStoreProvider StoreProvider { get; } = storeProvider;

    /// <inheritdoc />
    protected override ISurrogateKeyGenerator KeyGenerator { get; } = keyGenerator;

    /// <inheritdoc />
    protected override IPageCursorProtector CursorProtector { get; } = cursorProtector;

    /// <inheritdoc />
    protected override OpenIdDbContext DbContext { get; } = openIdDbContext;

    /// <inheritdoc />
    protected override ValueTask<PersistedFederatedIdentity> MapFromEntityAsync(
        FederatedIdentityEntity entity,
        CancellationToken cancellationToken
    )
    {
        var persistedFederatedIdentity = new PersistedFederatedIdentity
        {
            TenantId = entity.Tenant.TenantId,
            FederatedIdentityId = entity.FederatedIdentityId,
            PrincipalId = entity.FederatedPrincipal.PrincipalId,
            ConnectionId = entity.Connection.ConnectionId,
            Issuer = entity.Issuer,
            Subject = entity.Subject,
            JoinKey = entity.JoinKey,
            ConcurrencyToken = entity.ConcurrencyToken,
        };
        return ValueTask.FromResult(persistedFederatedIdentity);
    }

    /// <inheritdoc />
    protected override async ValueTask<FederatedIdentityEntity?> GetEntityOrDefaultAsync(
        Expression<Func<FederatedIdentityEntity, bool>> predicate,
        CancellationToken cancellationToken
    )
    {
        return GetLocalOrDefault(predicate)
            ?? await DbContext
                .FederatedIdentities.Include(entity => entity.FederatedPrincipal)
                .Include(entity => entity.Connection)
                .Include(entity => entity.Tenant)
                .FirstOrDefaultAsync(predicate, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask<IReadOnlyList<FederatedIdentityEntity>> GetEntityPageAsync(
        Guid? afterId,
        int take,
        CancellationToken cancellationToken
    )
    {
        var query = DbContext
            .FederatedIdentities.Include(entity => entity.FederatedPrincipal)
            .Include(entity => entity.Connection)
            .Include(entity => entity.Tenant)
            .AsQueryable();

        if (afterId is { } id)
        {
            query = query.Where(entity => entity.Id > id);
        }

        return await query.OrderBy(entity => entity.Id).Take(take).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override Guid GetSortKey(FederatedIdentityEntity entity) => entity.Id;

    /// <inheritdoc />
    public async ValueTask AddAsync(
        PersistedFederatedIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var tenantEntity = await GetTenantEntityAsync(identity, cancellationToken);

        var normalizedPrincipalId = Normalize(identity.PrincipalId);
        var principalEntity =
            GetLocalPrincipalOrDefault(normalizedPrincipalId)
            ?? await DbContext.FederatedPrincipals.SingleOrDefaultAsync(
                principal => principal.NormalizedPrincipalId == normalizedPrincipalId,
                cancellationToken
            )
            ?? throw new InvalidOperationException(
                $"A federated principal with '{identity.PrincipalId}' was not found."
            );

        var normalizedConnectionId = Normalize(identity.ConnectionId);
        var connectionEntity =
            GetLocalConnectionOrDefault(normalizedConnectionId)
            ?? await DbContext.Connections.SingleOrDefaultAsync(
                connection => connection.NormalizedConnectionId == normalizedConnectionId,
                cancellationToken
            )
            ?? throw new InvalidOperationException(
                $"A connection with '{identity.ConnectionId}' was not found."
            );

        // The interceptor leaves a pre-seeded insert token intact (ADR-1012).
        identity.ConcurrencyToken = NextConcurrencyToken();

        var entity = new FederatedIdentityEntity
        {
            Id = NextId(),
            TenantId = tenantEntity.Id,
            NormalizedTenantId = tenantEntity.NormalizedTenantId,
            FederatedIdentityId = identity.FederatedIdentityId,
            NormalizedFederatedIdentityId = Normalize(identity.FederatedIdentityId),
            FederatedPrincipalId = principalEntity.Id,
            ConnectionId = connectionEntity.Id,
            Issuer = identity.Issuer,
            Subject = identity.Subject,
            NormalizedSubject = Normalize(identity.Subject),
            JoinKey = identity.JoinKey,
            NormalizedJoinKey = Normalize(identity.JoinKey),
            ConcurrencyToken = identity.ConcurrencyToken,
            FederatedPrincipal = principalEntity,
            Connection = connectionEntity,
            Tenant = tenantEntity,
        };

        await DbContext.FederatedIdentities.AddAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<PersistedFederatedIdentity?> GetOrDefaultAsync(
        string federatedIdentityId,
        CancellationToken cancellationToken
    )
    {
        var normalizedFederatedIdentityId = Normalize(federatedIdentityId);
        var entity = await GetEntityOrDefaultAsync(
            identity => identity.NormalizedFederatedIdentityId == normalizedFederatedIdentityId,
            cancellationToken
        );
        return entity is null ? null : await MapFromEntityAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<PersistedFederatedIdentity?> GetByConnectionSubjectAsync(
        string connectionId,
        string subject,
        CancellationToken cancellationToken
    )
    {
        var normalizedConnectionId = Normalize(connectionId);
        var normalizedSubject = Normalize(subject);
        var entity = await GetEntityOrDefaultAsync(
            identity =>
                identity.Connection.NormalizedConnectionId == normalizedConnectionId
                && identity.NormalizedSubject == normalizedSubject,
            cancellationToken
        );
        return entity is null ? null : await MapFromEntityAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedFederatedIdentity>> GetByConnectionSubjectsAsync(
        string connectionId,
        IReadOnlyCollection<string> subjects,
        CancellationToken cancellationToken
    )
    {
        if (subjects.Count == 0)
        {
            return [];
        }

        var normalizedConnectionId = Normalize(connectionId);
        var normalizedSubjects = new List<string>(subjects.Count);
        foreach (var subject in subjects)
        {
            var normalizedSubject = Normalize(subject);
            normalizedSubjects.Add(normalizedSubject);
        }

        var entities = await DbContext
            .FederatedIdentities.Include(entity => entity.FederatedPrincipal)
            .Include(entity => entity.Connection)
            .Include(entity => entity.Tenant)
            .Where(entity =>
                entity.Connection.NormalizedConnectionId == normalizedConnectionId
                && normalizedSubjects.Contains(entity.NormalizedSubject)
            )
            .ToListAsync(cancellationToken);

        var result = new List<PersistedFederatedIdentity>(entities.Count);
        foreach (var entity in entities)
        {
            var mapped = await MapFromEntityAsync(entity, cancellationToken);
            result.Add(mapped);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<bool> AnyByConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken
    )
    {
        var normalizedConnectionId = Normalize(connectionId);
        return await DbContext.FederatedIdentities.AnyAsync(
            entity => entity.Connection.NormalizedConnectionId == normalizedConnectionId,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedFederatedIdentity>> GetByPrincipalAsync(
        string principalId,
        CancellationToken cancellationToken
    )
    {
        var normalizedPrincipalId = Normalize(principalId);
        var entities = await DbContext
            .FederatedIdentities.Include(entity => entity.FederatedPrincipal)
            .Include(entity => entity.Connection)
            .Include(entity => entity.Tenant)
            .Where(entity =>
                entity.FederatedPrincipal.NormalizedPrincipalId == normalizedPrincipalId
            )
            .ToListAsync(cancellationToken);

        var result = new List<PersistedFederatedIdentity>(entities.Count);
        foreach (var entity in entities)
        {
            var mapped = await MapFromEntityAsync(entity, cancellationToken);
            result.Add(mapped);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedFederatedIdentity>> GetByJoinKeyAsync(
        string joinKey,
        CancellationToken cancellationToken
    )
    {
        var normalizedJoinKey = Normalize(joinKey);
        var entities = await DbContext
            .FederatedIdentities.Include(entity => entity.FederatedPrincipal)
            .Include(entity => entity.Connection)
            .Include(entity => entity.Tenant)
            .Where(entity => entity.NormalizedJoinKey == normalizedJoinKey)
            .ToListAsync(cancellationToken);

        var result = new List<PersistedFederatedIdentity>(entities.Count);
        foreach (var entity in entities)
        {
            var mapped = await MapFromEntityAsync(entity, cancellationToken);
            result.Add(mapped);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(
        string federatedIdentityId,
        CancellationToken cancellationToken
    )
    {
        var normalizedFederatedIdentityId = Normalize(federatedIdentityId);
        var entity = await GetEntityOrDefaultAsync(
            identity => identity.NormalizedFederatedIdentityId == normalizedFederatedIdentityId,
            cancellationToken
        );
        if (entity is null)
        {
            return;
        }

        DbContext.FederatedIdentities.Remove(entity);
    }

    private FederatedPrincipalEntity? GetLocalPrincipalOrDefault(string? normalizedPrincipalId) =>
        DbContext
            .Set<FederatedPrincipalEntity>()
            .Local.FirstOrDefault(principal =>
                principal.NormalizedPrincipalId == normalizedPrincipalId
            );

    private ConnectionEntity? GetLocalConnectionOrDefault(string normalizedConnectionId) =>
        DbContext
            .Set<ConnectionEntity>()
            .Local.FirstOrDefault(connection =>
                connection.NormalizedConnectionId == normalizedConnectionId
            );
}
