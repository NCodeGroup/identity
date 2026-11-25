// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Publishes and reads the documents that the control-plane cell replicates to every cell through the directory.
/// Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface IReplicatedDocumentDirectory
{
    /// <summary>
    /// Publishes a document. The directory keeps it only when its revision is higher than the stored one.
    /// </summary>
    /// <param name="document">The <see cref="ReplicatedDocument"/> to publish.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, whose result is the
    /// revision the directory stores after the call.</returns>
    ValueTask<long> PublishAsync(ReplicatedDocument document, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a document.
    /// </summary>
    /// <param name="kind">The kind of the document.</param>
    /// <param name="key">The key of the document within its kind.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, whose result is the
    /// <see cref="ReplicatedDocument"/>, or <c>null</c> when the directory holds none.</returns>
    ValueTask<ReplicatedDocument?> GetOrDefaultAsync(
        string kind,
        string key,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Lists a page of the revisions of the documents the directory holds, ordered by kind and then by key.
    /// </summary>
    /// <param name="continuation">The <see cref="ReplicatedRevisionPage.Continuation"/> of the previous page, or
    /// <c>null</c> for the first page.</param>
    /// <param name="limit">The largest number of documents the page holds.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, whose result is the
    /// <see cref="ReplicatedRevisionPage"/> whose documents have a <c>null</c>
    /// <see cref="ReplicatedDocument.Content"/>.</returns>
    ValueTask<ReplicatedRevisionPage> ListRevisionsAsync(
        string? continuation,
        int limit,
        CancellationToken cancellationToken
    );
}
