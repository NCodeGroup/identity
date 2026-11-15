// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Clients;

/// <summary>
/// Represents the request body to create a new OpenID Client.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class CreateClientRequest
{
    /// <summary>
    /// Gets the client's identifier, or <c>null</c> when the server generates one. Only the platform's machine client of
    /// a cell may set it, as <c>cell-</c> followed by the cell's name; every other request leaves it <c>null</c>.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client is created in a disabled state.
    /// </summary>
    public required bool IsDisabled { get; init; }

    /// <summary>
    /// Gets the identifier of the resource server the client acts for, or <c>null</c> for an ordinary client. A client
    /// that acts for a resource server is that resource server's credential at the token introspection endpoint — where
    /// it learns about the access tokens issued for the resource server — and is accepted at no other endpoint. The
    /// resource server must belong to the tenant and must not be a system resource server, and the caller must be
    /// allowed to update it. The binding cannot be changed after the client is created.
    /// </summary>
    public string? ResourceServerId { get; init; }

    /// <summary>
    /// Gets the initial JSON settings for the client.
    /// </summary>
    public required JsonElement Settings { get; init; }

    /// <summary>
    /// Gets the client's public JSON Web Key Set, for <c>private_key_jwt</c> client authentication, or <c>null</c> when
    /// the client registers none.
    /// </summary>
    public JsonElement? Jwks { get; init; }
}
