// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the identifiers that <see cref="ITenantDirectory.ReserveAsync"/> reserves for a tenant about to be
/// created.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record TenantReservation
{
    /// <summary>
    /// Gets the identifier of the tenant.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the issuer of the tenant, or <c>null</c> when the tenant has none. A tenant without an issuer reserves its
    /// identifier alone.
    /// </summary>
    public string? Issuer { get; init; }

    /// <summary>
    /// Gets the identifier of the team that owns the tenant, or <c>null</c> when no team the directory holds a record
    /// of owns it, as for a tenant of the platform's own team (<see cref="TenantReservationRules.GetTeamId"/>).
    /// </summary>
    public string? TeamId { get; init; }

    /// <summary>
    /// Gets the name of the cell that will hold the tenant.
    /// </summary>
    public required string Cell { get; init; }
}
