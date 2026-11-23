// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Reports that a local connection has dependents while its store holds any local account of it, which keeps the
/// connection from being deleted.
/// </summary>
// See ADR-1535.
internal sealed class LocalAccountConnectionDependentSource(
    LocalAccountStoreCatalog localAccountStoreCatalog
) : IConnectionDependentSource
{
    private LocalAccountStoreCatalog LocalAccountStoreCatalog { get; } = localAccountStoreCatalog;

    /// <inheritdoc />
    public async ValueTask<bool> HasDependentsAsync(
        IStoreManager storeManager,
        PersistedConnection connection,
        CancellationToken cancellationToken
    )
    {
        var isLocal = string.Equals(
            connection.Strategy,
            ConnectionStrategyNames.Local,
            StringComparison.Ordinal
        );
        if (!isLocal)
        {
            return false;
        }

        var store = LocalAccountStoreCatalog.GetStore(storeManager, connection);
        var pageRequest = new PageRequest { Limit = 1 };
        var page = await store.GetPageAsync(
            connection.ConnectionId,
            pageRequest,
            cancellationToken
        );
        return page.Items.Count > 0;
    }
}
