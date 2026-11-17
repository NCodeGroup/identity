// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Applies the team changes of the directory's change feed to the cell's team rows. The feed's position lives in
/// <see cref="IDirectoryFeedCheckpoint"/>, not in the team rows. Register a custom implementation to replace the
/// default behavior.
/// </summary>
[PublicAPI]
public interface ITeamDocumentApplier
{
    /// <summary>
    /// Applies a published team document: it inserts or updates the team's row, announces the change so caches evict
    /// the team, and mirrors the root key the document holds for the cell's region into the team's owner root key,
    /// unless the team is disabled. A document whose sequence the row already carries or exceeds changes nothing. A
    /// document whose parent team the cell does not mirror changes nothing and is logged, because the directory lists
    /// the team again after its parent's latest change.
    /// </summary>
    /// <param name="document">The <see cref="TeamDocument"/> to apply.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask ApplyAsync(TeamDocument document, CancellationToken cancellationToken);

    /// <summary>
    /// Applies the retirement of a team: it removes the team's row, or disables it when tenants or teams still name it,
    /// and announces the change so caches evict the team. A removed team's owner root key is removed, and a disabled
    /// team keeps it. A team without a row, or whose row already carries the retirement's sequence or a later one,
    /// changes nothing, so a retirement that the directory delivers again is not written, announced or logged twice. A
    /// disabled team records the retirement's sequence on its row.
    /// </summary>
    /// <param name="teamId">The identifier of the retired team.</param>
    /// <param name="sequence">The sequence of the directory change that retired the team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RetireAsync(string teamId, long sequence, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the owner root key of each mirrored team to the reference that the team's row holds for the cell's
    /// region: it sets a key that differs, removes a key that the row doesn't hold, and leaves a key that agrees. A
    /// team whose key fails validation is logged and left, and the others still reconcile. A disabled team changes
    /// nothing. It also removes the owner root key of a team that has no row, which finishes a retirement whose key
    /// removal failed.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask ReconcileRootKeysAsync(CancellationToken cancellationToken);
}
