// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

/// <summary>
/// Represents an entity framework data contract for a tenant identifier that the directory reserved, placed in a cell
/// or retired. A retired record holds no issuer, because the issuer of a retired tenant is recorded in a
/// <see cref="RetiredIssuerRecord"/>.
/// </summary>
public sealed class TenantRecord
{
    /// <summary>
    /// The state of a tenant whose identifier is reserved and that no cell has placed.
    /// </summary>
    public const string StateReserved = "reserved";

    /// <summary>
    /// The state of a tenant that a cell placed.
    /// </summary>
    public const string StatePlaced = "placed";

    /// <summary>
    /// The state of a tenant that was deleted. Its identifier is never reissued.
    /// </summary>
    public const string StateRetired = "retired";

    /// <summary>
    /// Gets the surrogate identifier for this record.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public required string TenantId { get; set; }

    /// <summary>
    /// Gets or sets the value of <see cref="TenantId"/> normalized so that lookups are case-insensitive.
    /// </summary>
    public required string NormalizedTenantId { get; set; }

    /// <summary>
    /// Gets or sets the issuer of the tenant, or <c>null</c> when the tenant has none.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Gets or sets the value of <see cref="Issuer"/> normalized so that lookups are case-insensitive.
    /// </summary>
    public string? NormalizedIssuer { get; set; }

    /// <summary>
    /// Gets or sets the normalized identifier of the team that holds the tenant, or <c>null</c> when it has none.
    /// </summary>
    public string? NormalizedTeamId { get; set; }

    /// <summary>
    /// Gets or sets the name of the cell that holds the tenant, or <c>null</c> when no cell does.
    /// </summary>
    public string? Cell { get; set; }

    /// <summary>
    /// Gets or sets the state of the tenant, one of <see cref="StateReserved"/>, <see cref="StatePlaced"/> and
    /// <see cref="StateRetired"/>.
    /// </summary>
    public required string State { get; set; }

    /// <summary>
    /// Gets or sets when the record was created.
    /// </summary>
    public DateTimeOffset CreatedWhen { get; set; }

    /// <summary>
    /// Gets or sets when the record last changed.
    /// </summary>
    public DateTimeOffset ModifiedWhen { get; set; }
}
