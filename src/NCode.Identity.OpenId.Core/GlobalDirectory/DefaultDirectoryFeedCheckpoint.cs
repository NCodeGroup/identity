// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="IDirectoryFeedCheckpoint"/>. It reads and records the cell's one
/// checkpoint through <see cref="IDirectoryFeedCheckpointStore"/> in an unscoped ambient tenant scope, because the
/// checkpoint belongs to the cell and not to a tenant. Recording a sequence saves its own store manager, so the
/// checkpoint is committed when the call returns.
/// </summary>
// See ADR-1416.
internal sealed class DefaultDirectoryFeedCheckpoint(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor
) : IDirectoryFeedCheckpoint
{
    /// <summary>
    /// The reason that the checkpoint gives when it opens an unscoped ambient tenant scope to read and write the cell's
    /// checkpoint.
    /// </summary>
    private const string UnscopedReason = "directory feed checkpoint";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public async ValueTask<long> GetAppliedSequenceAsync(CancellationToken cancellationToken)
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IDirectoryFeedCheckpointStore>();

        return await store.GetAppliedSequenceAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask RecordAppliedSequenceAsync(
        long sequence,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IDirectoryFeedCheckpointStore>();

        await store.RecordAppliedSequenceAsync(sequence, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);
    }
}
