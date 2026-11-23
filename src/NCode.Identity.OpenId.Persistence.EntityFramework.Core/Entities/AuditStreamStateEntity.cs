// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.EntityFramework.Configuration;
using NCode.Identity.OpenId.Persistence.EntityFramework.Entities;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

/// <summary>
/// Represents an entity framework data contract for the delivery state of an <see cref="AuditStreamEntity"/>: how far
/// the stream has delivered, when it next attempts a delivery, how its deliveries have failed, and which replica holds
/// its lease. The state has its own concurrency token, so the delivery and the management of a stream never contend on
/// one. The complimentary DTO for this entity is <c>PersistedAuditStreamState</c>.
/// </summary>
// See ADR-1534.
[Index(nameof(AuditStreamId), IsUnique = true)]
[Index(nameof(NextAttemptTime))]
public sealed class AuditStreamStateEntity : ISupportTenantEntity, ISupportConcurrencyToken
{
    /// <summary>
    /// Gets the surrogate identifier for this entity.
    /// </summary>
    [Key]
    [SurrogateKey]
    public required Guid Id { get; init; }

    /// <inheritdoc />
    [ForeignKey(nameof(Tenant))]
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Gets the owning tenant's normalized identifier, denormalized onto this entity so the tenant-scoping global query
    /// filter compares a single indexed column without a navigation join.
    /// </summary>
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceId)]
    public required string NormalizedTenantId { get; init; }

    /// <summary>
    /// Gets the foreign key for the stream this state belongs to.
    /// </summary>
    [ForeignKey(nameof(AuditStream))]
    public required Guid AuditStreamId { get; init; }

    /// <summary>
    /// Gets or sets the time at which the store stored the last record that the stream delivered.
    /// </summary>
    public required DateTimeOffset PositionStoredTime { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the last audit event that the stream delivered, the tie-breaker of the position
    /// among records stored at the same time.
    /// </summary>
    public required Guid PositionEventId { get; set; }

    /// <summary>
    /// Gets or sets the time at which the stream next attempts a delivery.
    /// </summary>
    public required DateTimeOffset NextAttemptTime { get; set; }

    /// <summary>
    /// Gets or sets the count of deliveries that have failed in a row.
    /// </summary>
    public required int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Gets or sets the time of the last successful delivery, or <see langword="null"/> when there has been none.
    /// </summary>
    public DateTimeOffset? LastDeliveryTime { get; set; }

    /// <summary>
    /// Gets or sets the time of the last failed delivery, or <see langword="null"/> when there has been none.
    /// </summary>
    public DateTimeOffset? LastFailureTime { get; set; }

    /// <summary>
    /// Gets or sets a short description of the last failed delivery, or <see langword="null"/> when there has been
    /// none.
    /// </summary>
    [MaxLength(OpenIdMaxLengths.AuditStreamError)]
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the replica that holds the stream's lease, or <see langword="null"/> when no
    /// replica does.
    /// </summary>
    [Unicode(false)]
    [MaxLength(OpenIdMaxLengths.ReplicaId)]
    public string? LeaseHolder { get; set; }

    /// <summary>
    /// Gets or sets the time at which the stream's lease expires, or <see langword="null"/> when no replica holds it.
    /// </summary>
    public DateTimeOffset? LeaseExpiryTime { get; set; }

    //

    /// <inheritdoc />
    [Unicode(false)]
    [MaxLength(MaxLengths.ConcurrencyToken)]
    [ConcurrencyCheck]
    public required string ConcurrencyToken { get; set; }

    // navigational properties

    /// <inheritdoc />
    public required TenantEntity Tenant { get; init; }

    /// <summary>
    /// Gets the navigation property for the stream this state belongs to.
    /// </summary>
    public required AuditStreamEntity AuditStream { get; init; }
}
