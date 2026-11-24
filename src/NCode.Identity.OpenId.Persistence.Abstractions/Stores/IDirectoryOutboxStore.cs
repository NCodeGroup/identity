// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Provides an abstraction for a store which persists the documents that the control-plane cell has committed and still
/// has to publish to the global directory, and the latest revision of each.
/// </summary>
/// <remarks>
/// This store is not tenant-scoped. It does not save changes: the caller's store manager commits them.
/// </remarks>
[PublicAPI]
public interface IDirectoryOutboxStore : IStore
{
    /// <summary>
    /// Lists the entries that wait to be published.
    /// </summary>
    /// <param name="limit">The maximum number of entries to return.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the entries ordered
    /// by kind, key and revision.</returns>
    ValueTask<IReadOnlyList<PersistedDirectoryOutboxEntry>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes the entries of one document up to a revision when the unit of work is saved.
    /// </summary>
    /// <param name="kind">The kind of the document.</param>
    /// <param name="key">The key of the document within its kind.</param>
    /// <param name="revision">The highest revision to remove, which the directory has acknowledged.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RemoveThroughAsync(
        string kind,
        string key,
        long revision,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Lists the latest revision of every document that the cell publishes.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// revisions.</returns>
    ValueTask<IReadOnlyList<PersistedSourceRevision>> ListSourceRevisionsAsync(
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Composes a document whole from the rows the database holds now and adds it to the outbox at a revision, and sets
    /// the document's source revision to it, when the unit of work is saved. A team's authority document whose team the
    /// cell does not hold is added as retired.
    /// </summary>
    /// <param name="kind">The kind of the document.</param>
    /// <param name="key">The key of the document within its kind.</param>
    /// <param name="revision">The revision to publish the document at.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask EnqueueCurrentAsync(
        string kind,
        string key,
        long revision,
        CancellationToken cancellationToken
    );
}
