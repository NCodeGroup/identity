// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Provides an abstraction for a store which persists <see cref="PersistedConnection"/> instances. Lookups see a
/// connection added earlier in the same unit of work.
/// </summary>
[PublicAPI]
public interface IConnectionStore : IStore<PersistedConnection>
{
    /// <summary>
    /// Gets a single page of <see cref="PersistedConnection"/> instances ordered by their identifier, using keyset
    /// (cursor) pagination.
    /// </summary>
    /// <param name="page">The <see cref="PageRequest"/> describing the pagination window (cursor and limit).</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the page and the
    /// cursor for the next page.</returns>
    ValueTask<PagedResult<PersistedConnection>> GetPageAsync(
        PageRequest page,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets every connection of the tenant, enabled or not, ordered by <see cref="PersistedConnection.Name"/>
    /// ordinally. The result includes a connection added earlier in the same unit of work.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// connections.</returns>
    ValueTask<IReadOnlyList<PersistedConnection>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the enabled connections of the tenant, optionally limited to a set of names, ordered by
    /// <see cref="PersistedConnection.Name"/> ordinally. Names are matched case-insensitively, as the connection store
    /// compares them. The result reads the database, so it omits a connection added earlier in the same unit of work.
    /// </summary>
    /// <param name="names">The names to limit the result to, or <see langword="null"/> for every enabled connection; an
    /// empty set matches no connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// connections.</returns>
    ValueTask<IReadOnlyList<PersistedConnection>> GetEnabledAsync(
        IReadOnlyCollection<string>? names,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the connections of the tenant whose strategy is the named one, enabled or not, ordered by
    /// <see cref="PersistedConnection.Name"/> ordinally. The result reads the database, so it omits a connection added
    /// earlier in the same unit of work.
    /// </summary>
    /// <param name="strategy">The name of the strategy, compared ordinally.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// connections.</returns>
    ValueTask<IReadOnlyList<PersistedConnection>> GetByStrategyAsync(
        string strategy,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Attempts to get a <see cref="PersistedConnection"/> from the store by its opaque identifier. The lookup sees a
    /// connection added earlier in the same unit of work.
    /// </summary>
    /// <param name="connectionId">The opaque identifier of the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="PersistedConnection"/> if found; otherwise <c>null</c>.</returns>
    ValueTask<PersistedConnection?> GetOrDefaultAsync(
        string connectionId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Attempts to get a <see cref="PersistedConnection"/> from the store by its name, compared case-insensitively. The
    /// lookup sees a connection added earlier in the same unit of work.
    /// </summary>
    /// <param name="name">The name of the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="PersistedConnection"/> if found; otherwise <c>null</c>.</returns>
    ValueTask<PersistedConnection?> GetByNameOrDefaultAsync(
        string name,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Updates the JSON settings of an existing connection, enforcing optimistic concurrency against the settings
    /// concurrency token and bumping it on success.
    /// </summary>
    /// <param name="persistedConnectionSettings">The <see cref="PersistedConnectionSettings"/> to update.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask UpdateSettingsAsync(
        PersistedConnectionSettings persistedConnectionSettings,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes a connection from the store.
    /// </summary>
    /// <param name="connectionId">The opaque identifier of the connection to remove.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RemoveAsync(string connectionId, CancellationToken cancellationToken);
}
