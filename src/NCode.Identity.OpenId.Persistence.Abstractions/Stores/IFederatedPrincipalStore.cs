// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Provides an abstraction for a store which persists <see cref="PersistedFederatedPrincipal"/> instances: the global,
/// platform-owned human actors that authority references.
/// </summary>
[PublicAPI]
public interface IFederatedPrincipalStore : IStore
{
    /// <summary>
    /// Adds a new federated principal to the store.
    /// </summary>
    /// <param name="principal">The <see cref="PersistedFederatedPrincipal"/> to add.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask AddAsync(PersistedFederatedPrincipal principal, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing federated principal in the store, such as its metadata bags or its block state.
    /// </summary>
    /// <param name="principal">The <see cref="PersistedFederatedPrincipal"/> to update.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask UpdateAsync(
        PersistedFederatedPrincipal principal,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Attempts to get a federated principal from the store by its opaque identifier.
    /// </summary>
    /// <param name="principalId">The opaque identifier of the principal.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// <see cref="PersistedFederatedPrincipal"/> if found; otherwise <c>null</c>.</returns>
    ValueTask<PersistedFederatedPrincipal?> GetOrDefaultAsync(
        string principalId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the identifiers, as stored, of the federated principals that have any of the specified identifiers, in one
    /// call. It reads none of the principals' metadata.
    /// </summary>
    /// <param name="principalIds">The opaque identifiers of the principals.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the stored
    /// identifiers in no particular order, or an empty list when <paramref name="principalIds"/> is empty.</returns>
    ValueTask<IReadOnlyList<string>> GetExistingIdsAsync(
        IReadOnlyCollection<string> principalIds,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes a federated principal from the store, together with the consents and grants of the principal in its
    /// tenant. The role assignments the principal holds, in every tenant, are removed with
    /// <see cref="IRoleAssignmentStore.RemoveByPrincipalAsync"/>.
    /// </summary>
    /// <param name="principalId">The opaque identifier of the principal to remove.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RemoveAsync(string principalId, CancellationToken cancellationToken);
}
