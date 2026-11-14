// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ICellDirectory"/> of a cell that reaches the global directory service over HTTP. Each
/// method is one call under <c>directory/v1/cells</c>: <c>PUT {name}</c>, <c>GET</c> and <c>GET {name}</c>. A
/// <c>404</c> answer to a lookup means the cell is not registered. Every failure of the directory is a
/// <see cref="DirectoryUnavailableException"/>.
/// </summary>
// See ADR-1411.
internal sealed class RemoteCellDirectory(DirectoryHttpClient client) : ICellDirectory
{
    private const string Prefix = "cells";

    private DirectoryHttpClient Client { get; } = client;

    /// <inheritdoc />
    public async ValueTask RegisterAsync(
        CellDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var response = await Client.SendAsync(
            HttpMethod.Put,
            $"{Prefix}/{DirectoryHttpClient.Segment(descriptor.Name)}",
            descriptor,
            cancellationToken
        );

        response.EnsureSuccess("register a cell");
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CellDescriptor>> ListAsync(
        CancellationToken cancellationToken
    )
    {
        var response = await Client.SendAsync(
            HttpMethod.Get,
            Prefix,
            body: null,
            cancellationToken
        );

        response.EnsureSuccess("list the cells");
        return response.Read<List<CellDescriptor>>();
    }

    /// <inheritdoc />
    public async ValueTask<CellDescriptor?> GetOrDefaultAsync(
        string name,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var response = await Client.SendAsync(
            HttpMethod.Get,
            $"{Prefix}/{DirectoryHttpClient.Segment(name)}",
            body: null,
            cancellationToken
        );

        if (response.IsNotFound)
            return null;

        response.EnsureSuccess("get a cell");
        return response.Read<CellDescriptor>();
    }
}
