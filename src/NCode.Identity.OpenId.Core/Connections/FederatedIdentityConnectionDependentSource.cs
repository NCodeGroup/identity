// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Reports the federated identities of a connection as its dependents.
/// </summary>
// See ADR-1535.
internal sealed class FederatedIdentityConnectionDependentSource : IConnectionDependentSource
{
    /// <inheritdoc />
    public ValueTask<bool> HasDependentsAsync(
        IStoreManager storeManager,
        PersistedConnection connection,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(storeManager);
        ArgumentNullException.ThrowIfNull(connection);

        var store = storeManager.GetStore<IFederatedIdentityStore>();
        return store.AnyByConnectionAsync(connection.ConnectionId, cancellationToken);
    }
}
