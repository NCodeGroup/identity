// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents a scope of a resource server in a <see cref="TenantConfigurationDocument"/>.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TenantConfigurationScope
{
    /// <summary>
    /// Gets the value of the scope.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// Gets the description of the scope, or <see langword="null"/> when it has none.
    /// </summary>
    public required string? Description { get; init; }

    /// <summary>
    /// Gets a value indicating whether the scope is a system scope of the platform.
    /// </summary>
    public required bool IsSystem { get; init; }
}
