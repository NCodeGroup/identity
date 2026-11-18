// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Reserves team identifiers platform-wide, publishes team documents to the cells, and remembers the identifiers of
/// deleted teams. An implementation that cannot reach the directory throws <see cref="DirectoryUnavailableException"/>.
/// Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface ITeamDirectory
{
    /// <summary>
    /// Reserves the identifier of a team about to be created. The reservation is refused when a published team holds
    /// the identifier or it is retired. Reserving an identifier whose reservation is held and not yet published
    /// succeeds, so a reservation that runs again after it took effect is not refused.
    /// </summary>
    /// <param name="teamId">The identifier of the team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ReservationResult"/> that is reserved, or a conflict with its detail.</returns>
    ValueTask<ReservationResult> ReserveAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a team document, which every cell mirrors into its team rows.
    /// </summary>
    /// <param name="document">The <see cref="TeamDocument"/> to publish.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask PublishAsync(TeamDocument document, CancellationToken cancellationToken);

    /// <summary>
    /// Releases the reservation of a team whose creation failed, so its identifier can be reserved again.
    /// </summary>
    /// <param name="teamId">The identifier of the team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask ReleaseAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>
    /// Retires the identifier of a deleted team. A retired identifier is never reissued.
    /// </summary>
    /// <param name="teamId">The identifier of the deleted team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RetireAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether the specified team identifier belonged to a deleted team.
    /// </summary>
    /// <param name="teamId">The team identifier to look up.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns><c>true</c> when a deleted team had the identifier; otherwise, <c>false</c>.</returns>
    ValueTask<bool> IsTeamIdRetiredAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the published document of a team.
    /// </summary>
    /// <param name="teamId">The identifier of the team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="TeamDocument"/> of the team, or <c>null</c> when the team has none.</returns>
    ValueTask<TeamDocument?> GetOrDefaultAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the changes to team documents that follow the specified sequence, in ascending order of sequence. A team's
    /// change always follows the latest change of each of its ancestors.
    /// </summary>
    /// <param name="after">
    /// The sequence of the last change the caller has applied, or <c>0</c> for every change.
    /// </param>
    /// <param name="limit">The most changes to return.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The <see cref="DirectoryChange"/> entries after <paramref name="after"/>, at most <paramref name="limit"/> of
    /// them, and fewer when an implementation bounds the size of a page, which still holds at least one change; none
    /// only when no change follows <paramref name="after"/>.
    /// </returns>
    ValueTask<IReadOnlyList<DirectoryChange>> ListChangesAsync(
        long after,
        int limit,
        CancellationToken cancellationToken
    );
}
