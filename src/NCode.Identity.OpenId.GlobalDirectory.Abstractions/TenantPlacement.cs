// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Records the cell that holds a tenant.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record TenantPlacement
{
    /// <summary>
    /// Gets the identifier of the tenant.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the issuer of the tenant, or <c>null</c> when the tenant has none.
    /// </summary>
    public string? Issuer { get; init; }

    /// <summary>
    /// Gets the name of the cell that holds the tenant.
    /// </summary>
    public required string Cell { get; init; }

    /// <summary>
    /// Gets the region that the cell holding the tenant registered with the directory, or an empty string until that
    /// cell registers.
    /// </summary>
    public required string Region { get; init; }
}
