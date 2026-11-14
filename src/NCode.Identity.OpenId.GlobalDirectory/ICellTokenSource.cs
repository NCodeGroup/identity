// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Supplies the access tokens that a cell presents to the directory service and to the control plane, as the cell's
/// machine client. Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface ICellTokenSource
{
    /// <summary>
    /// Gets the access token that the cell's machine client holds for an audience and a scope, requested with the
    /// client-credentials grant and <c>private_key_jwt</c> authentication from the issuer named by
    /// <c>Cell:ControlPlane:Issuer</c>. An implementation that cannot obtain a token throws
    /// <see cref="DirectoryUnavailableException"/>.
    /// </summary>
    /// <param name="audience">The audience of the access token, such as <c>urn:ncode:directory</c>.</param>
    /// <param name="scope">The scope that the access token carries, such as <c>directory</c>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The access token.</returns>
    ValueTask<string> GetAccessTokenAsync(
        string audience,
        string scope,
        CancellationToken cancellationToken
    );
}
