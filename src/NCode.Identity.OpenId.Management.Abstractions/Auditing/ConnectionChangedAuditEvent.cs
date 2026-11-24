// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Events.Audit;

namespace NCode.Identity.OpenId.Management.Auditing;

/// <summary>
/// An audit event raised when an administrator creates, updates, or deletes a tenant's connection, or updates its
/// settings. The specific transition is carried by <see cref="ResourceChangeAuditEvent.ChangeType"/> and the owning
/// tenant by <see cref="IAuditEvent.TenantId"/>.
/// </summary>
[PublicAPI]
public sealed record ConnectionChangedAuditEvent : ResourceChangeAuditEvent
{
    /// <inheritdoc />
    public override string ResourceType => ManagementResourceTypes.Connection;
}
