// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Supplies the access token that a cell presents to the directory service, as the cell's machine client. Register a
/// custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface ICellTokenSource
{
    /// <summary>
    /// Gets the access token that the cell's machine client holds for the directory, requested from the directory's
    /// token endpoint with the client-credentials grant and <c>private_key_jwt</c> authentication. An implementation
    /// that cannot obtain a token throws <see cref="DirectoryUnavailableException"/>, and
    /// <see cref="DirectoryClientNotRegisteredException"/> when the directory has no registration for the cell.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The access token.</returns>
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
