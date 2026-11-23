// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Reports whether records depend on a connection, which keeps the connection from being deleted.
/// </summary>
// See ADR-1535.
[PublicAPI]
public interface IConnectionDependentSource
{
    /// <summary>
    /// Determines whether any record depends on the connection.
    /// </summary>
    /// <param name="storeManager">The <see cref="IStoreManager"/> of the unit of work, bound to the connection's tenant.</param>
    /// <param name="connection">The connection that would be deleted.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing
    /// <see langword="true"/> when a record depends on the connection; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> HasDependentsAsync(
        IStoreManager storeManager,
        PersistedConnection connection,
        CancellationToken cancellationToken
    );
}
