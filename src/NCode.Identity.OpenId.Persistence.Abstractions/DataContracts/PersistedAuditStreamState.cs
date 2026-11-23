// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains the delivery state of an audit stream: how far the stream has delivered, when it next attempts a delivery,
/// how its deliveries have failed, and which replica holds its lease. The state is kept apart from the stream's
/// configuration so that the delivery and the management of the stream never contend on one concurrency token.
/// </summary>
// See ADR-1534.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedAuditStreamState
{
    /// <summary>
    /// Gets the identifier of the tenant the stream belongs to.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the identifier of the stream.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets or sets the time at which the store stored the last record that the stream delivered.
    /// </summary>
    public required DateTimeOffset PositionStoredTime { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the last audit event that the stream delivered, the tie-breaker of the position
    /// among records stored at the same time.
    /// </summary>
    [SuppressMessage(
        "Design",
        "NCID0005",
        Justification = "The audit event's public identifier, which its event time already dates; it is not a surrogate key."
    )]
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
    [MaxLength(OpenIdMaxLengths.ReplicaId)]
    public string? LeaseHolder { get; set; }

    /// <summary>
    /// Gets or sets the time at which the stream's lease expires, or <see langword="null"/> when no replica holds it.
    /// </summary>
    public DateTimeOffset? LeaseExpiryTime { get; set; }

    /// <summary>
    /// Gets or sets the concurrency token of the state, which an update checks.
    /// </summary>
    public required string ConcurrencyToken { get; set; }
}
