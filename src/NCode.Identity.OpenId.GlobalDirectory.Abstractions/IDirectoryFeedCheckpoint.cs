// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the highest sequence of the directory's change feed that this cell has applied, which the feed reads before
/// a pass and raises after each page. Register a custom implementation to replace the default behavior.
/// </summary>
// See ADR-1416.
[PublicAPI]
public interface IDirectoryFeedCheckpoint
{
    /// <summary>
    /// Gets the highest sequence of the directory's change feed that this cell has applied.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the highest applied
    /// sequence, or <c>0</c> before the cell has applied its first change.</returns>
    ValueTask<long> GetAppliedSequenceAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records <paramref name="sequence"/> as the highest sequence of the directory's change feed that this cell has
    /// applied. A sequence at or below the recorded one changes nothing, so the checkpoint never lowers.
    /// </summary>
    /// <param name="sequence">The sequence of the directory change that the cell applied.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RecordAppliedSequenceAsync(long sequence, CancellationToken cancellationToken);
}
