// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ICellDirectory"/> of a cell that reaches the global directory service over HTTP. Each method
/// is one call under <c>directory/v1/cells</c>: <c>PUT {name}</c>, <c>PUT {name}/registration</c>,
/// <c>DELETE {name}</c>, <c>GET</c> and <c>GET {name}</c>. A <c>404</c> answer to a lookup means the cell is not
/// registered, and a <c>409</c> answer to a write is a <see cref="CellDirectoryConflictException"/> that carries the
/// directory's detail. Every other failure of the directory is a <see cref="DirectoryUnavailableException"/>.
/// </summary>
// See ADR-1411.
internal sealed class RemoteCellDirectory(DirectoryHttpClient client) : ICellDirectory
{
    private const string Prefix = "cells";

    private DirectoryHttpClient Client { get; } = client;

    /// <inheritdoc />
    public async ValueTask ReportAsync(
        CellDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var segment = DirectoryHttpClient.Segment(descriptor.Name);
        var response = await Client.SendAsync(
            HttpMethod.Put,
            $"{Prefix}/{segment}",
            descriptor,
            cancellationToken
        );

        ThrowIfConflict(response, "The directory refused to take the cell's report.");
        response.EnsureSuccess("report a cell");
    }

    /// <inheritdoc />
    public async ValueTask RegisterAsync(
        CellRegistration registration,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(registration);

        var segment = DirectoryHttpClient.Segment(registration.Name);
        var body = new RegistrationBody
        {
            Region = registration.Region,
            Kind = registration.Kind,
            Jwks = registration.Jwks,
        };
        var response = await Client.SendAsync(
            HttpMethod.Put,
            $"{Prefix}/{segment}/registration",
            body,
            cancellationToken
        );

        ThrowIfConflict(response, "The directory refused to register the cell.");
        response.EnsureSuccess("register a cell");
    }

    /// <inheritdoc />
    public async ValueTask RetireAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var segment = DirectoryHttpClient.Segment(name);
        var response = await Client.SendAsync(
            HttpMethod.Delete,
            $"{Prefix}/{segment}",
            body: null,
            cancellationToken
        );

        ThrowIfConflict(response, "The directory refused to retire the cell.");
        response.EnsureSuccess("retire a cell");
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

        var segment = DirectoryHttpClient.Segment(name);
        var response = await Client.SendAsync(
            HttpMethod.Get,
            $"{Prefix}/{segment}",
            body: null,
            cancellationToken
        );

        if (response.IsNotFound)
            return null;

        response.EnsureSuccess("get a cell");
        return response.Read<CellDescriptor>();
    }

    /// <summary>
    /// Throws the conflict that the directory answered.
    /// </summary>
    /// <param name="response">The <see cref="DirectoryResponse"/> to inspect.</param>
    /// <param name="fallback">The detail to use when the answer carries none.</param>
    /// <exception cref="CellDirectoryConflictException">The directory answered <c>409</c>.</exception>
    private static void ThrowIfConflict(DirectoryResponse response, string fallback)
    {
        if (!response.IsConflict)
            return;

        var detail = response.ReadProblemDetail(fallback);
        throw new CellDirectoryConflictException(detail);
    }

    /// <summary>
    /// Describes the body of a cell registration, which names the cell by its route.
    /// </summary>
    private sealed record RegistrationBody
    {
        public required string Region { get; init; }

        public required string Kind { get; init; }

        public required System.Text.Json.JsonElement Jwks { get; init; }
    }
}
