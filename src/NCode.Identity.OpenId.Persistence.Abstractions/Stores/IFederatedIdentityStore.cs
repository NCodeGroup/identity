// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Provides an abstraction for a store which persists <see cref="PersistedFederatedIdentity"/> instances: the external
/// connection identities owned by federated principals. Each identity is uniquely identified by its
/// <c>(connection, subject)</c> pair.
/// </summary>
[PublicAPI]
public interface IFederatedIdentityStore : IStore
{
    /// <summary>
    /// Adds a new federated identity to the store.
    /// </summary>
    /// <param name="identity">The <see cref="PersistedFederatedIdentity"/> to add.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask AddAsync(PersistedFederatedIdentity identity, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to get a federated identity from the store by its opaque identifier.
    /// </summary>
    /// <param name="federatedIdentityId">The opaque identifier of the federated identity.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="PersistedFederatedIdentity"/> if found; otherwise <c>null</c>.</returns>
    ValueTask<PersistedFederatedIdentity?> GetOrDefaultAsync(
        string federatedIdentityId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Attempts to get a federated identity from the store by its <c>(connection, subject)</c> natural key (the lookup
    /// used to resolve an authenticated caller to a principal).
    /// </summary>
    /// <param name="connectionId">The identifier of the connection that authenticated the identity.</param>
    /// <param name="subject">The subject value issued by the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="PersistedFederatedIdentity"/> if found; otherwise <c>null</c>.</returns>
    ValueTask<PersistedFederatedIdentity?> GetByConnectionSubjectAsync(
        string connectionId,
        string subject,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the federated identities that the specified connection issued for any of the specified subjects, in one
    /// call; a subject with no identity contributes none.
    /// </summary>
    /// <param name="connectionId">The identifier of the connection that authenticated the identities.</param>
    /// <param name="subjects">The subject values issued by the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the identities
    /// that match the connection and any of the <paramref name="subjects"/>.</returns>
    ValueTask<IReadOnlyList<PersistedFederatedIdentity>> GetByConnectionSubjectsAsync(
        string connectionId,
        IReadOnlyCollection<string> subjects,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Determines whether any federated identity belongs to the specified connection.
    /// </summary>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing <c>true</c> when at
    /// least one identity belongs to the connection.</returns>
    ValueTask<bool> AnyByConnectionAsync(string connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets every federated identity owned by the specified principal.
    /// </summary>
    /// <param name="principalId">The identifier of the owning principal.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the principal's
    /// identities.</returns>
    ValueTask<IReadOnlyList<PersistedFederatedIdentity>> GetByPrincipalAsync(
        string principalId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets every federated identity that asserts the specified join key (such as a verified email address). The
    /// linking policy uses this to deterministically attach a new identity to an existing principal.
    /// </summary>
    /// <param name="joinKey">The join key to match.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the identities that
    /// assert the join key.</returns>
    ValueTask<IReadOnlyList<PersistedFederatedIdentity>> GetByJoinKeyAsync(
        string joinKey,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes a federated identity from the store by its opaque identifier.
    /// </summary>
    /// <param name="federatedIdentityId">The opaque identifier of the federated identity to remove.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RemoveAsync(string federatedIdentityId, CancellationToken cancellationToken);
}
