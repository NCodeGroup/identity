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
/// Provides a default implementation of <see cref="IConnectionStore"/> that uses Entity Framework Core for persistence.
/// A lookup sees a connection added earlier in the same unit of work.
/// </summary>
internal sealed class ConnectionStore(
    IStoreProvider storeProvider,
    ISurrogateKeyGenerator keyGenerator,
    IPageCursorProtector cursorProtector,
    OpenIdDbContext openIdDbContext
) : CoreBaseStoreWithResourceId<PersistedConnection, ConnectionEntity>, IConnectionStore
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
    protected override ValueTask<PersistedConnection> MapFromEntityAsync(
        ConnectionEntity entity,
        CancellationToken cancellationToken
    )
    {
        var tenantId = entity.Tenant.TenantId;
        var persistedConnection = new PersistedConnection
        {
            TenantId = tenantId,
            ConnectionId = entity.ConnectionId,
            ConcurrencyToken = entity.ConcurrencyToken,
            Name = entity.Name,
            Strategy = entity.Strategy,
            DisplayName = entity.DisplayName,
            IsSystem = entity.IsSystem,
            IsEnabled = entity.IsEnabled,
            Options = entity.OptionsJson,
            Settings = new PersistedConnectionSettings
            {
                TenantId = tenantId,
                ConnectionId = entity.ConnectionId,
                ConcurrencyToken = entity.SettingsConcurrencyToken,
                Value = entity.SettingsJson,
            },
        };
        return ValueTask.FromResult(persistedConnection);
    }

    /// <inheritdoc />
    protected override async ValueTask<ConnectionEntity?> GetEntityOrDefaultAsync(
        Expression<Func<ConnectionEntity, bool>> predicate,
        CancellationToken cancellationToken
    )
    {
        return GetLocalOrDefault(predicate)
            ?? await DbContext
                .Connections.Include(entity => entity.Tenant)
                .FirstOrDefaultAsync(predicate, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask<ConnectionEntity?> GetEntityOrDefaultAsync(
        string resourceId,
        CancellationToken cancellationToken
    )
    {
        var normalizedConnectionId = Normalize(resourceId);
        return await GetEntityOrDefaultAsync(
            entity => entity.NormalizedConnectionId == normalizedConnectionId,
            cancellationToken
        );
    }

    /// <inheritdoc />
    protected override async ValueTask<IReadOnlyList<ConnectionEntity>> GetEntityPageAsync(
        Guid? afterId,
        int take,
        CancellationToken cancellationToken
    )
    {
        var query = DbContext.Connections.Include(entity => entity.Tenant).AsQueryable();

        if (afterId is { } id)
        {
            query = query.Where(entity => entity.Id > id);
        }

        return await query.OrderBy(entity => entity.Id).Take(take).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override Guid GetSortKey(ConnectionEntity entity) => entity.Id;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedConnection>> GetAllAsync(
        CancellationToken cancellationToken
    )
    {
        var entities = await DbContext
            .Connections.Include(entity => entity.Tenant)
            .ToListAsync(cancellationToken);

        // A connection added earlier in the same unit of work is not in the database yet.
        var knownIds = entities.Select(entity => entity.Id).ToHashSet();
        var addedIds = DbContext
            .Connections.Local.Where(entity =>
                DbContext.Entry(entity).State == EntityState.Added && !knownIds.Contains(entity.Id)
            )
            .Select(entity => entity.Id)
            .ToList();
        foreach (var addedId in addedIds)
        {
            var added = GetLocalOrDefault(entity => entity.Id == addedId);
            if (added is not null)
            {
                entities.Add(added);
            }
        }

        // The database collation decides the order of names, so the ordinal order is fixed in memory.
        var items = new List<PersistedConnection>(entities.Count);
        foreach (var entity in entities.OrderBy(entity => entity.Name, StringComparer.Ordinal))
        {
            var item = await MapFromEntityAsync(entity, cancellationToken);
            items.Add(item);
        }

        return items;
    }

    /// <inheritdoc />
    public async ValueTask<PersistedConnection?> GetByNameOrDefaultAsync(
        string name,
        CancellationToken cancellationToken
    )
    {
        var normalizedName = Normalize(name);
        return await GetOrDefaultAsync(
            entity => entity.NormalizedName == normalizedName,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public override async ValueTask AddAsync(
        PersistedConnection persistedConnection,
        CancellationToken cancellationToken
    )
    {
        var tenantEntity = await GetTenantEntityAsync(
            persistedConnection.TenantId,
            cancellationToken
        );

        // The interceptor leaves a pre-seeded insert token intact (ADR-1012); the sub-resource version column is manual.
        persistedConnection.ConcurrencyToken = NextConcurrencyToken();
        persistedConnection.Settings.ConcurrencyToken = NextConcurrencyToken();

        var connectionEntity = new ConnectionEntity
        {
            Id = NextId(),
            TenantId = tenantEntity.Id,
            NormalizedTenantId = tenantEntity.NormalizedTenantId,
            ConnectionId = persistedConnection.ConnectionId,
            NormalizedConnectionId = Normalize(persistedConnection.ConnectionId),
            Name = persistedConnection.Name,
            NormalizedName = Normalize(persistedConnection.Name),
            Strategy = persistedConnection.Strategy,
            DisplayName = persistedConnection.DisplayName,
            IsSystem = persistedConnection.IsSystem,
            IsEnabled = persistedConnection.IsEnabled,
            OptionsJson = persistedConnection.Options,
            SettingsJson = persistedConnection.Settings.Value,
            ConcurrencyToken = persistedConnection.ConcurrencyToken,
            SettingsConcurrencyToken = persistedConnection.Settings.ConcurrencyToken,
            Tenant = tenantEntity,
        };

        await DbContext.Connections.AddAsync(connectionEntity, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask UpdateAsync(
        PersistedConnection persistedConnection,
        CancellationToken cancellationToken
    )
    {
        var connectionEntity = await GetEntityAsync(
            persistedConnection.ConnectionId,
            cancellationToken
        );

        if (
            !string.Equals(
                persistedConnection.ConcurrencyToken,
                connectionEntity.ConcurrencyToken,
                StringComparison.Ordinal
            )
        )
        {
            throw new StoreConcurrencyException(
                "The connection has been modified by another process. Please reload and try again."
            );
        }

        // The row-level ConcurrencyToken is regenerated by the interceptor on save (ADR-1012).
        connectionEntity.DisplayName = persistedConnection.DisplayName;
        connectionEntity.IsEnabled = persistedConnection.IsEnabled;
        connectionEntity.OptionsJson = persistedConnection.Options;
    }

    /// <inheritdoc />
    public async ValueTask UpdateSettingsAsync(
        PersistedConnectionSettings persistedConnectionSettings,
        CancellationToken cancellationToken
    )
    {
        var connectionId = persistedConnectionSettings.ConnectionId;
        var connectionEntity = await GetEntityAsync(connectionId, cancellationToken);

        if (
            !string.Equals(
                persistedConnectionSettings.ConcurrencyToken,
                connectionEntity.SettingsConcurrencyToken,
                StringComparison.Ordinal
            )
        )
        {
            throw new StoreConcurrencyException(
                $"The settings for the connection with ConnectionId='{connectionId}' have been modified by another process. Please reload and try again."
            );
        }

        var nextConcurrencyToken = NextConcurrencyToken();

        connectionEntity.SettingsConcurrencyToken = nextConcurrencyToken;
        connectionEntity.SettingsJson = persistedConnectionSettings.Value;

        DbContext.Connections.Update(connectionEntity);

        persistedConnectionSettings.ConcurrencyToken = nextConcurrencyToken;
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string connectionId, CancellationToken cancellationToken)
    {
        var connectionEntity = await GetEntityOrDefaultAsync(connectionId, cancellationToken);
        if (connectionEntity is null)
        {
            return;
        }

        var tenantKey = connectionEntity.TenantId;
        var normalizedConnectionId = connectionEntity.NormalizedConnectionId;

        // The role assignments at the connection's node are stored under its tenant and removed with it.
        var roleAssignments = await DbContext
            .RoleAssignments.Where(entity =>
                entity.TenantId == tenantKey
                && entity.ResourceType == OpenIdResourceTypes.Connection
                && entity.NormalizedResourceId == normalizedConnectionId
            )
            .ToListAsync(cancellationToken);
        DbContext.RoleAssignments.RemoveRange(roleAssignments);

        DbContext.Connections.Remove(connectionEntity);
    }
}
