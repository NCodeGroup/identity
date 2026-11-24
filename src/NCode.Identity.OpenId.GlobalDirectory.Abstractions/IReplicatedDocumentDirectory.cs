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
    /// Lists the revision of every document the directory holds.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, whose result is the
    /// documents with a <c>null</c> <see cref="ReplicatedDocument.Content"/>.</returns>
    ValueTask<IReadOnlyList<ReplicatedDocument>> ListRevisionsAsync(
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Applies the replicated documents of the directory's change feed to the cell's database. Register a custom
/// implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface IReplicatedDocumentApplier
{
    /// <summary>
    /// Applies a replicated document. A document whose revision is not higher than the stored one changes nothing.
    /// </summary>
    /// <param name="document">The <see cref="ReplicatedDocument"/> to apply.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask ApplyAsync(ReplicatedDocument document, CancellationToken cancellationToken);
}
