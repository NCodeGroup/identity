// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Represents one page of a forward read of a tenant's audit records, with the position of its last record.
/// </summary>
// See ADR-1534.
[PublicAPI]
public sealed class AuditRecordStreamPage
{
    /// <summary>
    /// Gets the records of the page, oldest first by the time the store stored them and then by event identifier.
    /// </summary>
    public required IReadOnlyList<PersistedAuditRecord> Items { get; init; }

    /// <summary>
    /// Gets the time the store stored the last record of the page, or <see langword="null"/> for an empty page.
    /// </summary>
    public DateTimeOffset? LastStoredTime { get; init; }

    /// <summary>
    /// Gets the event identifier of the last record of the page, or <see langword="null"/> for an empty page.
    /// </summary>
    public Guid? LastEventId { get; init; }
}
