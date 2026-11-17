// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Provides an abstraction for a store which persists the highest sequence of the directory's change feed that the cell
/// has applied.
/// </summary>
/// <remarks>
/// A cell has one checkpoint. It is not tenant-scoped, and it advances for every change the cell applies, including a
/// retirement that leaves no team or tenant row behind to carry the change's sequence.
/// </remarks>
// See ADR-1416.
[PublicAPI]
public interface IDirectoryFeedCheckpointStore : IStore
{
    /// <summary>
    /// Gets the highest sequence of the directory's change feed that the cell has applied.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the highest applied
    /// sequence, or <c>0</c> before the cell has applied its first change.</returns>
    ValueTask<long> GetAppliedSequenceAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Raises the highest applied sequence to <paramref name="sequence"/> when the unit of work is saved, creating the
    /// checkpoint on first use. A sequence at or below the stored one changes nothing, so the checkpoint never lowers.
    /// </summary>
    /// <param name="sequence">The sequence of the directory change that the cell applied.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RecordAppliedSequenceAsync(long sequence, CancellationToken cancellationToken);
}
