// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Stores;

/// <summary>
/// Reads the key sets that the directory verifies a cell's client assertions with.
/// </summary>
internal interface ICellClientKeyStore
{
    /// <summary>
    /// Gets the key set that a registered cell's client assertions are verified with.
    /// </summary>
    /// <param name="name">The name of the cell.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The JSON Web Key Set of the cell, or <c>null</c> when no unretired cell is registered under the
    /// name.</returns>
    ValueTask<JsonElement?> GetCellClientKeysAsync(
        string name,
        CancellationToken cancellationToken
    );
}
