// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Secrets.Persistence.DataContracts;

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Finds the root key that wraps the master keys of an owner: the owner's own root key, or else the root key of the
/// nearest ancestor that has one.
/// </summary>
// See ADR-1414.
internal interface INearestOwnerRootKeyResolver
{
    /// <summary>
    /// Finds the nearest owner root key that wraps the master keys of an owner. A root key that is being removed wraps
    /// nothing, so the search passes it by.
    /// </summary>
    /// <param name="store">
    /// The store that reads the owner root keys, which keeps the read in the caller's unit of work.
    /// </param>
    /// <param name="owner">The owner whose master keys the root key wraps.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// The root key, whose <see cref="PersistedOwnerRootKey.Owner"/> names the owner whose key it is, or
    /// <see langword="null"/> when no owner in the chain has one, which means the deployment's configured root key.
    /// </returns>
    ValueTask<PersistedOwnerRootKey?> FindNearestAsync(
        IOwnerRootKeyStore store,
        SecretOwner owner,
        CancellationToken cancellationToken
    );
}
