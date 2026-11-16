// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Provides an abstraction for a store which persists <see cref="PersistedTeam"/> instances.
/// </summary>
// See ADR-1411
[PublicAPI]
public interface ITeamStore : IStore<PersistedTeam>
{
    /// <summary>
    /// Gets a single page of <see cref="PersistedTeam"/> instances ordered by their identifier, using keyset
    /// (cursor) pagination.
    /// </summary>
    /// <param name="page">The <see cref="PageRequest"/> describing the pagination window (cursor and limit).</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the page of
    /// <see cref="PersistedTeam"/> instances and the cursor for the next page.</returns>
    ValueTask<PagedResult<PersistedTeam>> GetPageAsync(
        PageRequest page,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Attempts to get a <see cref="PersistedTeam"/> instance from the store with the specified identifier.
    /// </summary>
    /// <param name="teamId">The identifier of the <see cref="PersistedTeam"/> instance to retrieve.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="PersistedTeam"/> instance matching the specified identifier if it exists.</returns>
    ValueTask<PersistedTeam?> GetOrDefaultAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a single page of the <see cref="PersistedTeam"/> instances directly beneath a team, ordered by their
    /// surrogate identifier, using keyset (cursor) pagination.
    /// </summary>
    /// <param name="teamId">The identifier of the parent team.</param>
    /// <param name="page">The <see cref="PageRequest"/> describing the pagination window (cursor and limit).</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the page of
    /// <see cref="PersistedTeam"/> instances and the cursor for the next page; an empty page when no team has the
    /// specified identifier.</returns>
    // See ADR-1426.
    ValueTask<PagedResult<PersistedTeam>> GetChildPageAsync(
        string teamId,
        PageRequest page,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets a team and every team above it, with the top-level team first and the team itself last.
    /// </summary>
    /// <param name="teamId">The identifier of the team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="PersistedTeam"/> instances from the top-level team down to the team; empty when no team has the
    /// specified identifier.</returns>
    // See ADR-1426.
    ValueTask<IReadOnlyList<PersistedTeam>> GetLineageAsync(
        string teamId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Counts every team beneath a team at every level.
    /// </summary>
    /// <param name="teamId">The identifier of the team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the number of
    /// teams beneath the team; zero when the team does not exist.</returns>
    // See ADR-1426.
    ValueTask<long> CountDescendantsAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the concurrency token of each listed team that exists, keyed by its identifier compared without regard
    /// to case; a team that does not exist has no entry.
    /// </summary>
    /// <param name="teamIds">The identifiers of the teams whose concurrency tokens to read.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the concurrency
    /// token of each listed team that exists.</returns>
    ValueTask<IReadOnlyDictionary<string, string>> GetConcurrencyTokensAsync(
        IReadOnlyCollection<string> teamIds,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the <see cref="PersistedTeam"/> instances with the specified identifiers in one read, ordered by their
    /// identifier; an identifier that has no team is skipped.
    /// </summary>
    /// <param name="teamIds">The identifiers of the teams to get.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="PersistedTeam"/> instances that exist.</returns>
    ValueTask<IReadOnlyList<PersistedTeam>> GetManyAsync(
        IReadOnlyCollection<string> teamIds,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the <see cref="PersistedTeamSettings"/> instance from the store with the specified identifier.
    /// </summary>
    /// <param name="teamId">The identifier of the OpenId Team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the <see cref="PersistedTeamSettings"/> for the specified identifier.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no team has the specified identifier.</exception>
    ValueTask<PersistedTeamSettings> GetSettingsAsync(
        string teamId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Attempts to get the <see cref="PersistedTeamSettings"/> instance from the store with the specified identifier.
    /// </summary>
    /// <param name="teamId">The identifier of the OpenId Team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="PersistedTeamSettings"/> if found; otherwise <c>null</c>.</returns>
    ValueTask<PersistedTeamSettings?> GetSettingsOrDefaultAsync(
        string teamId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Updates the JSON settings for an OpenId Team in the store.
    /// </summary>
    /// <param name="persistedTeamSettings">The <see cref="PersistedTeamSettings"/> instance to update.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask UpdateSettingsAsync(
        PersistedTeamSettings persistedTeamSettings,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Updates the effective settings, and their fingerprint, that the team's tenants and clients were last reconciled
    /// against.
    /// </summary>
    /// <param name="persistedTeam">The <see cref="PersistedTeam"/> instance carrying the reconciled settings.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask UpdateReconciledSettingsAsync(
        PersistedTeam persistedTeam,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Updates the root keys that the team brings, keyed by region. Only the map is written, and the team's other
    /// columns stay as they are.
    /// </summary>
    /// <param name="persistedTeam">The <see cref="PersistedTeam"/> instance carrying the root keys and the concurrency
    /// token it was read with.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    /// <exception cref="StoreConcurrencyException">Thrown when the team was modified since it was read.</exception>
    ValueTask UpdateRootKeysAsync(PersistedTeam persistedTeam, CancellationToken cancellationToken);

    /// <summary>
    /// Removes an OpenId Team from the store. The removal is rejected while any tenant or team still belongs to the
    /// team. Removing a team retires its identifier, so it is never reissued.
    /// </summary>
    /// <param name="teamId">The identifier of the OpenId Team to remove.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing <c>true</c>
    /// if the team was removed; otherwise <c>false</c> when no matching team existed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a tenant, team or machine client still belongs to the team.</exception>
    ValueTask<bool> RemoveAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether an OpenId Team still has dependents: any tenant, team or machine client that belongs to it.
    /// </summary>
    /// <param name="teamId">The identifier of the OpenId Team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing <c>true</c>
    /// when any tenant, team or machine client belongs to the team; otherwise <c>false</c> (including when no such team exists).</returns>
    ValueTask<bool> HasDependentsAsync(string teamId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the highest directory sequence that the teams' rows carry, which is the position in the directory's change
    /// feed that this store has applied.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the highest
    /// <see cref="PersistedTeam.DirectorySequence"/>, or <c>0</c> when no team exists.</returns>
    ValueTask<long> GetMaxDirectorySequenceAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Upserts the team named by the persisted team's <c>TeamId</c> from a team document that the directory published.
    /// The team row is left as it is when its <see cref="PersistedTeam.DirectorySequence"/> is at or above the
    /// document's. Otherwise a missing row is inserted, its display name, settings, parent and path are set from the
    /// document, and, when the parent changes, the paths of the teams beneath it are rewritten.
    /// </summary>
    /// <param name="persistedTeam">The <see cref="PersistedTeam"/> instance that carries the document's values and its
    /// sequence.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing <c>true</c> when
    /// the document was applied; otherwise <c>false</c> when the row already carries a sequence at or above the
    /// document's.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the parent team does not exist, or when the team or a
    /// team beneath it would be nested deeper than <see cref="TeamPaths.MaxDepth"/> levels.</exception>
    ValueTask<bool> MirrorAsync(PersistedTeam persistedTeam, CancellationToken cancellationToken);
}
