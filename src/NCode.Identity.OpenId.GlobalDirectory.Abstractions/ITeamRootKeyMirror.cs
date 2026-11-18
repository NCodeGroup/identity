// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Mirrors the root key that each mirrored team's row holds for the cell's region into the team's owner root key.
/// Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface ITeamRootKeyMirror
{
    /// <summary>
    /// Brings one team's owner root key to its committed row: it sets the key that the row holds for the cell's region
    /// when it differs from the key in place, removes the key when the row holds none or the team has no row, and
    /// leaves the key of a disabled team. It runs after the row is committed, on a token of its own bounded by the
    /// post-commit timeout, so the caller's cancellation drops nothing; a key that fails, or that the bound cuts, is
    /// logged and never thrown, and the next document for the team or the next reconcile retries it. A cancellation of
    /// the caller's own token propagates.
    /// </summary>
    /// <param name="teamId">The identifier of the team whose owner root key is mirrored.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask MirrorAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the owner root key of each mirrored team to the reference that the team's row holds for the cell's
    /// region: it sets a key that differs, removes a key that the row doesn't hold, and leaves a key that agrees. A
    /// team whose key fails validation is logged and left, and the others still reconcile. A disabled team changes
    /// nothing. It also removes the owner root key of a team that has no row, which finishes a retirement whose key
    /// removal failed.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask ReconcileAsync(CancellationToken cancellationToken);
}
