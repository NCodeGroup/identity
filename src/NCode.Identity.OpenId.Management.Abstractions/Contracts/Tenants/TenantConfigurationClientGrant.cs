// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents the grant of one client to one resource server in a <see cref="TenantConfigurationDocument"/>.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantConfigurationClientGrant
{
    /// <summary>
    /// Gets the identifier of the client that holds the grant.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Gets the identifier of the resource server the grant reaches.
    /// </summary>
    public required string ResourceServerId { get; init; }

    /// <summary>
    /// Gets the scopes the grant allows.
    /// </summary>
    public required List<string> Scopes { get; init; }
}
