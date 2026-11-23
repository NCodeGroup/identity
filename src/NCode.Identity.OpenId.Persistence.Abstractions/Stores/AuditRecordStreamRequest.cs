// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Describes one forward read of a tenant's audit records: the records the store stored after a position and before a
/// time, oldest first.
/// </summary>
// See ADR-1534.
[PublicAPI]
public readonly record struct AuditRecordStreamRequest
{
    /// <summary>
    /// Gets the identifier of the tenant whose records are read. A read never crosses tenants.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the time the store stored the last record already read. Only records stored after it, or at it with a
    /// greater event identifier, are returned.
    /// </summary>
    public required DateTimeOffset AfterStoredTime { get; init; }

    /// <summary>
    /// Gets the event identifier of the last record already read, which orders the records the store stored at the same
    /// time.
    /// </summary>
    public required Guid AfterEventId { get; init; }

    /// <summary>
    /// Gets the time before which a record must have been stored to be returned, exclusive.
    /// </summary>
    public required DateTimeOffset StoredBefore { get; init; }

    /// <summary>
    /// Gets the greatest number of records returned.
    /// </summary>
    public required int Limit { get; init; }
}
