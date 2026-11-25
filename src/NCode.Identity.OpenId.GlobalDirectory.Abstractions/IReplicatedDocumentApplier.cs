// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

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
