// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents a resource server and its scopes in a <see cref="TenantConfigurationDocument"/>.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantConfigurationResourceServer
{
    /// <summary>
    /// Gets the opaque identifier of the resource server.
    /// </summary>
    public required string ResourceServerId { get; init; }

    /// <summary>
    /// Gets the audience identifier of the resource server.
    /// </summary>
    public required string Identifier { get; init; }

    /// <summary>
    /// Gets the display name of the resource server.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a value indicating whether the resource server is a system resource server of the platform.
    /// </summary>
    public required bool IsSystem { get; init; }

    /// <summary>
    /// Gets a value indicating whether the resource server is disabled.
    /// </summary>
    public required bool IsDisabled { get; init; }

    /// <summary>
    /// Gets the resource server's settings document.
    /// </summary>
    public required JsonElement Settings { get; init; }

    /// <summary>
    /// Gets the scopes the resource server defines.
    /// </summary>
    public required List<TenantConfigurationScope> Scopes { get; init; }
}
