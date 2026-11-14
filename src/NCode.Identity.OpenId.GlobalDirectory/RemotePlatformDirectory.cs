// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the <see cref="IPlatformDirectory"/> of a cell that reaches the global directory service over HTTP. Each
/// method is one call under <c>directory/v1/platform/</c>: <c>PUT document</c> and <c>GET document</c>. A <c>404</c>
/// answer to a lookup means no platform document is published. Every failure of the directory is a
/// <see cref="DirectoryUnavailableException"/>.
/// </summary>
// See ADR-1411.
internal sealed class RemotePlatformDirectory(DirectoryHttpClient client) : IPlatformDirectory
{
    private const string Path = "platform/document";

    private DirectoryHttpClient Client { get; } = client;

    /// <inheritdoc />
    public async ValueTask PublishAsync(
        PlatformDocument document,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(document);

        var response = await Client.SendAsync(HttpMethod.Put, Path, document, cancellationToken);

        response.EnsureSuccess("publish the platform document");
    }

    /// <inheritdoc />
    public async ValueTask<PlatformDocument?> GetOrDefaultAsync(CancellationToken cancellationToken)
    {
        var response = await Client.SendAsync(HttpMethod.Get, Path, body: null, cancellationToken);

        if (response.IsNotFound)
            return null;

        response.EnsureSuccess("get the platform document");
        return response.Read<PlatformDocument>();
    }
}
