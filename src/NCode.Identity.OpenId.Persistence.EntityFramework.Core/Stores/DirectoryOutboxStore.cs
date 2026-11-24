// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Stores;

/// <summary>
/// Provides a default implementation of <see cref="IDirectoryOutboxStore"/> that uses Entity Framework Core for
/// persistence.
/// </summary>
/// <remarks>
/// This store does not save changes. The caller's store manager commits them.
/// </remarks>
internal class DirectoryOutboxStore(IStoreProvider storeProvider, OpenIdDbContext openIdDbContext)
    : IDirectoryOutboxStore
{
    private IStoreProvider StoreProvider { get; } = storeProvider;

    private OpenIdDbContext DbContext { get; } = openIdDbContext;

    /// <inheritdoc />
    public object? GetService(Type serviceType) => StoreProvider.GetService(serviceType);

    /// <inheritdoc />
    public TStore GetStore<TStore>()
        where TStore : IStore => StoreProvider.GetStore<TStore>();

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedDirectoryOutboxEntry>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken
    )
    {
        var entities = await DbContext
            .DirectoryOutbox.AsNoTracking()
            .OrderBy(entity => entity.Kind)
            .ThenBy(entity => entity.Key)
            .ThenBy(entity => entity.Revision)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities
            .Select(entity => new PersistedDirectoryOutboxEntry
            {
                Sequence = entity.Sequence,
                Kind = entity.Kind,
                Key = entity.Key,
                Revision = entity.Revision,
                ContentJson = entity.ContentJson,
                CreatedWhen = entity.CreatedWhen,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask RemoveThroughAsync(
        string kind,
        string key,
        long revision,
        CancellationToken cancellationToken
    )
    {
        var entities = await DbContext
            .DirectoryOutbox.Where(entity =>
                entity.Kind == kind && entity.Key == key && entity.Revision <= revision
            )
            .ToListAsync(cancellationToken);

        DbContext.DirectoryOutbox.RemoveRange(entities);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PersistedSourceRevision>> ListSourceRevisionsAsync(
        CancellationToken cancellationToken
    )
    {
        var entities = await DbContext
            .ReplicatedSourceRevisions.AsNoTracking()
            .OrderBy(entity => entity.Kind)
            .ThenBy(entity => entity.Key)
            .ToListAsync(cancellationToken);

        return entities
            .Select(entity => new PersistedSourceRevision
            {
                Kind = entity.Kind,
                Key = entity.Key,
                Revision = entity.Revision,
            })
            .ToList();
    }
}
