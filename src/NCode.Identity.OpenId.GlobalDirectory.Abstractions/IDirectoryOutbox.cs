// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Exposes the documents that the control-plane cell has committed and still has to publish to the directory.
/// Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface IDirectoryOutbox
{
    /// <summary>
    /// Lists the documents that wait to be published.
    /// </summary>
    /// <param name="limit">The maximum number of documents to return.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, whose result is the documents ordered by kind, key and revision.</returns>
    ValueTask<IReadOnlyList<ReplicatedDocument>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes the pending documents of one document identity up to a revision, which the directory has acknowledged.
    /// </summary>
    /// <param name="kind">The kind of the document.</param>
    /// <param name="key">The key of the document within its kind.</param>
    /// <param name="revision">The highest revision to remove.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask AcknowledgeAsync(
        string kind,
        string key,
        long revision,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Lists the latest revision of every document that the cell publishes.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, whose result is the documents with a <c>null</c> <see cref="ReplicatedDocument.Content"/>.</returns>
    ValueTask<IReadOnlyList<ReplicatedDocument>> ListLocalRevisionsAsync(
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Composes a document from the cell's current rows and adds it to the outbox at a revision, so the publisher sends
    /// it again.
    /// </summary>
    /// <param name="kind">The kind of the document.</param>
    /// <param name="key">The key of the document within its kind.</param>
    /// <param name="atRevision">The revision to publish the document at.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RepublishAsync(
        string kind,
        string key,
        long atRevision,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Wakes the publisher of the directory outbox after a commit wrote outbox entries.
/// Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface IDirectoryOutboxSignal
{
    /// <summary>
    /// Waits until the signal is raised or a time has passed.
    /// </summary>
    /// <param name="timeout">The longest time to wait.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Raises the signal, which wakes a waiting publisher or the next wait.
    /// </summary>
    void Notify();
}
