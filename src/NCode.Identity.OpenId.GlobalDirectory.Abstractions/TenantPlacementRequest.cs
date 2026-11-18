// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the request body that a cell sends to place a tenant it reserved through
/// <see cref="ITenantDirectory.PlaceAsync"/>. The directory takes the placement's region from the cell's registration.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record TenantPlacementRequest
{
    /// <summary>
    /// Gets the identifier of the tenant.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the issuer of the tenant.
    /// </summary>
    public required string Issuer { get; init; }

    /// <summary>
    /// Gets the name of the cell that places the tenant.
    /// </summary>
    public required string Cell { get; init; }
}
