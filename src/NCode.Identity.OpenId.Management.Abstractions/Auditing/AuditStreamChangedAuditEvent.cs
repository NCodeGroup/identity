// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Events.Audit;

namespace NCode.Identity.OpenId.Management.Auditing;

// See ADR-1534.

/// <summary>
/// An audit event raised when an administrator creates, updates, or deletes an audit stream, or rotates its signing
/// secret. The specific transition is carried by <see cref="ResourceChangeAuditEvent.ChangeType"/> and the owning
/// tenant by <see cref="IAuditEvent.TenantId"/>.
/// </summary>
[PublicAPI]
public sealed record AuditStreamChangedAuditEvent : ResourceChangeAuditEvent
{
    /// <inheritdoc />
    public override string ResourceType => ManagementResourceTypes.AuditStream;
}
