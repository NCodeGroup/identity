// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Provides an abstraction for a store which persists the replicas of the documents that the global directory
/// replicates into a cell, and the management keys that the cell refuses.
/// </summary>
/// <remarks>
/// This store is not tenant-scoped. It does not save changes: the caller's store manager commits them.
/// </remarks>
[PublicAPI]
public interface IReplicatedDocumentStore : IStore
{
    /// <summary>
    /// Gets the replica of a document.
    /// </summary>
    /// <param name="kind">The kind of the document.</param>
    /// <param name="key">The key of the document within its kind.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the replica, or
    /// <c>null</c> when the cell holds none.</returns>
    ValueTask<PersistedReplicatedDocument?> GetOrDefaultAsync(
        string kind,
        string key,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Lists the replicas of one kind.
    /// </summary>
    /// <param name="kind">The kind of the documents.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the replicas.</returns>
    ValueTask<IReadOnlyList<PersistedReplicatedDocument>> ListByKindAsync(
        string kind,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Stores a document when the unit of work is saved, only when it is newer than the replica: its revision is
    /// higher, or it is equal and retires a replica that still holds content.
    /// </summary>
    /// <param name="document">The document to apply.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing <c>true</c> when the
    /// document replaced or created the replica, and <c>false</c> when the replica is as new or newer.</returns>
    ValueTask<bool> TryApplyAsync(
        PersistedReplicatedDocument document,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Lists the management keys that the cell refuses.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the revoked keys.</returns>
    ValueTask<IReadOnlyList<PersistedRevokedManagementKey>> ListRevokedKeysAsync(
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Records a revoked management key when the unit of work is saved. A key that is already recorded keeps its first
    /// record.
    /// </summary>
    /// <param name="revokedKey">The revoked key.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask AddRevokedKeyAsync(
        PersistedRevokedManagementKey revokedKey,
        CancellationToken cancellationToken
    );
}
