// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Stores;

/// <summary>
/// Provides a default implementation of <see cref="IDirectoryFeedCheckpointStore"/> that uses Entity Framework Core for
/// persistence. The checkpoint is a singleton: its row carries the key
/// <see cref="DirectoryFeedCheckpointEntity.SingletonKey"/>, which is the primary key of the table.
/// </summary>
/// <remarks>
/// This store does not save changes. The caller's store manager commits them, which is where a second row for the cell
/// is rejected by the primary key and where a raise from a stale read is rejected by the concurrency token.
/// </remarks>
internal class DirectoryFeedCheckpointStore(
    IStoreProvider storeProvider,
    OpenIdDbContext openIdDbContext
) : IDirectoryFeedCheckpointStore
{
    private IStoreProvider StoreProvider { get; } = storeProvider;

    private OpenIdDbContext DbContext { get; } = openIdDbContext;

    /// <inheritdoc />
    public object? GetService(Type serviceType) => StoreProvider.GetService(serviceType);

    /// <inheritdoc />
    public TStore GetStore<TStore>()
        where TStore : IStore => StoreProvider.GetStore<TStore>();

    /// <inheritdoc />
    public async ValueTask<long> GetAppliedSequenceAsync(CancellationToken cancellationToken)
    {
        var entity = await DbContext
            .DirectoryFeedCheckpoints.AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Key == DirectoryFeedCheckpointEntity.SingletonKey,
                cancellationToken
            );

        return entity?.AppliedSequence ?? 0;
    }

    /// <inheritdoc />
    public async ValueTask RecordAppliedSequenceAsync(
        long sequence,
        CancellationToken cancellationToken
    )
    {
        var entity = await DbContext.DirectoryFeedCheckpoints.SingleOrDefaultAsync(
            entity => entity.Key == DirectoryFeedCheckpointEntity.SingletonKey,
            cancellationToken
        );

        if (entity is null)
        {
            // Assign the row token up front; the interceptor leaves a pre-seeded insert token intact (ADR-1012).
            entity = new DirectoryFeedCheckpointEntity
            {
                Key = DirectoryFeedCheckpointEntity.SingletonKey,
                AppliedSequence = sequence,
                ConcurrencyToken = Guid.NewGuid().ToString("N"),
            };

            await DbContext.DirectoryFeedCheckpoints.AddAsync(entity, cancellationToken);
            return;
        }

        // A sequence at or below the stored one changes nothing, so the row is never touched and its token stays.
        if (sequence > entity.AppliedSequence)
            entity.AppliedSequence = sequence;
    }
}
