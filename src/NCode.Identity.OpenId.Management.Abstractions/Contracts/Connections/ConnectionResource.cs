// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Management.Contracts.Connections;

/// <summary>
/// Represents the REST resource for a connection: a tenant-scoped, named resource of a registered strategy through
/// which end users sign in.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class ConnectionResource
    : ISupportConnectionId,
        ISupportTenantId,
        ISupportConcurrencyToken
{
    /// <inheritdoc cref="ISupportTenantId.TenantId"/>
    public required string TenantId { get; init; }

    /// <inheritdoc cref="ISupportConnectionId.ConnectionId"/>
    public required string ConnectionId { get; init; }

    /// <inheritdoc cref="ISupportConcurrencyToken.ConcurrencyToken"/>
    public required string ConcurrencyToken { get; init; }

    /// <summary>
    /// Gets the name of this connection, unique within its tenant and immutable.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the human-readable name that the sign-in picker shows.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the name of the strategy this connection uses, which is immutable.
    /// </summary>
    public required string Strategy { get; init; }

    /// <summary>
    /// Gets a value indicating whether this connection is a reserved, system-owned connection that cannot be deleted.
    /// </summary>
    public required bool IsSystem { get; init; }

    /// <summary>
    /// Gets a value indicating whether this connection is enabled.
    /// </summary>
    public required bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the JSON object of strategy-specific options.
    /// </summary>
    public required JsonElement Options { get; init; }
}
