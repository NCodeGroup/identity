// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Reads and writes the local accounts of one connection of the ambient tenant as a whole, so a tenant's accounts,
/// with their credentials, move between cells in an encrypted transfer. A store that implements <see cref="ILocalAccountStore"/>
/// implements this seam on the same class.
/// </summary>
[PublicAPI]
public interface ILocalAccountTransferStore : IStore
{
    /// <summary>
    /// Gets every local account of a connection of the ambient tenant as a <see cref="LocalAccountTransferRecord"/>, ordered by
    /// identifier.
    /// </summary>
    /// <param name="connectionId">The identifier of the local connection whose accounts are read.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The <see cref="ValueTask"/> that represents the asynchronous operation, containing the records.
    /// </returns>
    ValueTask<IReadOnlyList<LocalAccountTransferRecord>> ExportAsync(
        string connectionId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Adds the records to a connection of the ambient tenant as local accounts with the identifiers, password hashes and security
    /// stamps they carry.
    /// </summary>
    /// <param name="connectionId">The identifier of the local connection that receives the accounts.</param>
    /// <param name="records">The records to add.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask ImportAsync(
        string connectionId,
        IReadOnlyList<LocalAccountTransferRecord> records,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes every local account of a connection of the ambient tenant.
    /// </summary>
    /// <param name="connectionId">The identifier of the local connection whose accounts are removed.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RemoveAllAsync(string connectionId, CancellationToken cancellationToken);
}
