// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Supplies the access token that the remote directory adapters present to the directory service. Register a custom
/// implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface IDirectoryTokenSource
{
    /// <summary>
    /// Gets a client-credentials access token for the directory audience, <c>urn:ncode:directory</c>, with the
    /// <c>directory</c> scope. An implementation that cannot obtain a token throws
    /// <see cref="DirectoryUnavailableException"/>.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The access token.</returns>
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
