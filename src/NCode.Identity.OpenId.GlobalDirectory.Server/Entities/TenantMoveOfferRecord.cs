// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

/// <summary>
/// Represents an entity framework data contract for the offer that the cell holding a tenant records to move the tenant
/// to another cell. A tenant has at most one offer, and the move consumes it.
/// </summary>
public sealed class TenantMoveOfferRecord
{
    /// <summary>
    /// Gets the surrogate identifier for this record.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the normalized identifier of the tenant that the offer is for.
    /// </summary>
    public required string NormalizedTenantId { get; set; }

    /// <summary>
    /// Gets or sets the name of the cell that holds the tenant and offers it.
    /// </summary>
    public required string FromCell { get; set; }

    /// <summary>
    /// Gets or sets the name of the cell that the tenant is offered to.
    /// </summary>
    public required string ToCell { get; set; }

    /// <summary>
    /// Gets or sets when the offer was recorded.
    /// </summary>
    public DateTimeOffset CreatedWhen { get; set; }

    /// <summary>
    /// Gets or sets when the offer expires.
    /// </summary>
    public DateTimeOffset ExpiresWhen { get; set; }
}
