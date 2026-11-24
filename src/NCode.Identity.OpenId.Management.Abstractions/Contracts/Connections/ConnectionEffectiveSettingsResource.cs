// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence;

namespace NCode.Identity.OpenId.Management.Contracts.Connections;

/// <summary>
/// Represents the REST resource for a connection's effective (merged) settings, which the tenant's effective settings
/// merged with the connection's persisted overrides give, as the authorization server resolves them at runtime.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class ConnectionEffectiveSettingsResource : ISupportConnectionId, ISupportTenantId
{
    /// <inheritdoc cref="ISupportTenantId.TenantId"/>
    public required string TenantId { get; init; }

    /// <inheritdoc cref="ISupportConnectionId.ConnectionId"/>
    public required string ConnectionId { get; init; }

    /// <summary>
    /// Gets the read-only merged (tenant effective settings + connection overrides) flat JSON settings object the
    /// authorization server resolves at runtime.
    /// </summary>
    public required JsonElement Settings { get; init; }
}
