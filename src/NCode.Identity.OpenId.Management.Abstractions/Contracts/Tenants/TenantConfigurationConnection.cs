// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents a connection in a <see cref="TenantConfigurationDocument"/>. A connection is matched by its name, so the
/// document holds no identifier of it.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantConfigurationConnection
{
    /// <summary>
    /// Gets the name of the connection, which is unique within its tenant.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the name of the strategy of the connection.
    /// </summary>
    public required string Strategy { get; init; }

    /// <summary>
    /// Gets the name that the sign-in picker shows for the connection.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the connection is enabled.
    /// </summary>
    public required bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the connection's options document, which its strategy validates.
    /// </summary>
    public required JsonElement Options { get; init; }

    /// <summary>
    /// Gets the connection's settings document.
    /// </summary>
    public required JsonElement Settings { get; init; }
}
