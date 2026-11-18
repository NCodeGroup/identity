// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents the configuration of one tenant that a promotion carries into another tenant of the same region: the
/// tenant's settings, its clients, their grants and its resource servers. The document holds no secret, key,
/// credential, member tenant identifier, concurrency token or principal identifier.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantConfigurationDocument
{
    /// <summary>
    /// Gets the identifier of the tenant whose configuration the document holds.
    /// </summary>
    public required string SourceTenantId { get; init; }

    /// <summary>
    /// Gets the region of the cell that exported the document.
    /// </summary>
    public required string Region { get; init; }

    /// <summary>
    /// Gets the tenant's settings document.
    /// </summary>
    public required JsonElement Settings { get; init; }

    /// <summary>
    /// Gets the tenant's clients that the host does not manage, without its machine clients.
    /// </summary>
    public required List<TenantConfigurationClient> Clients { get; init; }

    /// <summary>
    /// Gets the grants of the clients in <see cref="Clients"/>.
    /// </summary>
    public required List<TenantConfigurationClientGrant> ClientGrants { get; init; }

    /// <summary>
    /// Gets the tenant's resource servers with their scopes.
    /// </summary>
    public required List<TenantConfigurationResourceServer> ResourceServers { get; init; }
}
