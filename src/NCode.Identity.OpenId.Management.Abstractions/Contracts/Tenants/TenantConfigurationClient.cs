// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents a client in a <see cref="TenantConfigurationDocument"/>.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantConfigurationClient
{
    /// <summary>
    /// Gets the client identifier.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the client is disabled.
    /// </summary>
    public required bool IsDisabled { get; init; }

    /// <summary>
    /// Gets the identifier of the resource server the client acts for, or <see langword="null"/> when it acts for none.
    /// </summary>
    public required string? ResourceServerId { get; init; }

    /// <summary>
    /// Gets the client's settings document.
    /// </summary>
    public required JsonElement Settings { get; init; }
}
