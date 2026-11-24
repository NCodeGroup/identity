// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Stores;

/// <summary>
/// Provides a default implementation of <see cref="IReplicatedDocumentStore"/> that uses Entity Framework Core for
/// persistence.
/// </summary>
/// <remarks>
/// This store does not save changes. The caller's store manager commits them, which is where a second row for the same
/// document is rejected by the primary key and where an apply from a stale read is rejected by the concurrency token.
/// </remarks>
internal class ReplicatedDocumentStore(
    IStoreProvider storeProvider,
    OpenIdDbContext openIdDbContext
) : IReplicatedDocumentStore
{
    private IStoreProvider StoreProvider { get; } = storeProvider;

    private OpenIdDbContext DbContext { get; } = openIdDbContext;

    /// <inheritdoc />
    public object? GetService(Type serviceType) => StoreProvider.GetService(serviceType);

    /// <inheritdoc />
    public TStore GetStore<TStore>()
        where TStore : IStore => StoreProvider.GetStore<TStore>();

    /// <inheritdoc />
    public async ValueTask<PersistedReplicatedDocument?> GetOrDefaultAsync(
        string kind,
        string key,
        CancellationToken cancellationToken
    )
    {
        var entity = await DbContext
            .ReplicatedDocuments.AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Kind == kind && entity.Key == key,
                cancellationToken
            );

        return entity is null ? null : MapFromEntity(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedReplicatedDocument>> ListByKindAsync(
        string kind,
        CancellationToken cancellationToken
    )
    {
        var entities = await DbContext
            .ReplicatedDocuments.AsNoTracking()
            .Where(entity => entity.Kind == kind)
            .OrderBy(entity => entity.Key)
            .ToListAsync(cancellationToken);

        return entities.Select(MapFromEntity).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryApplyAsync(
        PersistedReplicatedDocument document,
        CancellationToken cancellationToken
    )
    {
        var entity = await DbContext.ReplicatedDocuments.SingleOrDefaultAsync(
            entity => entity.Kind == document.Kind && entity.Key == document.Key,
            cancellationToken
        );

        if (entity is null)
        {
            // Assign the row token up front; the interceptor leaves a pre-seeded insert token intact (ADR-1012).
            entity = new ReplicatedDocumentEntity
            {
                Kind = document.Kind,
                Key = document.Key,
                Revision = document.Revision,
                ContentJson = document.ContentJson,
                ConcurrencyToken = Guid.NewGuid().ToString("N"),
            };

            await DbContext.ReplicatedDocuments.AddAsync(entity, cancellationToken);
            return true;
        }

        var isNewer = document.Revision > entity.Revision;
        var isRetirement =
            document.Revision == entity.Revision
            && document.ContentJson is null
            && entity.ContentJson is not null;

        if (!isNewer && !isRetirement)
            return false;

        entity.Revision = document.Revision;
        entity.ContentJson = document.ContentJson;

        // Touch the row so the interceptor regenerates the ConcurrencyToken on save (ADR-1012).
        DbContext.ReplicatedDocuments.Update(entity);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedRevokedManagementKey>> ListRevokedKeysAsync(
        CancellationToken cancellationToken
    )
    {
        var entities = await DbContext
            .RevokedManagementKeys.AsNoTracking()
            .OrderBy(entity => entity.KeyId)
            .ToListAsync(cancellationToken);

        return entities
            .Select(entity => new PersistedRevokedManagementKey
            {
                KeyId = entity.KeyId,
                RevokedWhen = entity.RevokedWhen,
                Source = entity.Source,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask AddRevokedKeyAsync(
        PersistedRevokedManagementKey revokedKey,
        CancellationToken cancellationToken
    )
    {
        var exists = await DbContext.RevokedManagementKeys.AnyAsync(
            entity => entity.KeyId == revokedKey.KeyId,
            cancellationToken
        );

        if (exists)
            return;

        var entity = new RevokedManagementKeyEntity
        {
            KeyId = revokedKey.KeyId,
            RevokedWhen = revokedKey.RevokedWhen.ToUniversalTime(),
            Source = revokedKey.Source,
        };

        await DbContext.RevokedManagementKeys.AddAsync(entity, cancellationToken);
    }

    private static PersistedReplicatedDocument MapFromEntity(ReplicatedDocumentEntity entity) =>
        new()
        {
            Kind = entity.Kind,
            Key = entity.Key,
            Revision = entity.Revision,
            ContentJson = entity.ContentJson,
        };
}
