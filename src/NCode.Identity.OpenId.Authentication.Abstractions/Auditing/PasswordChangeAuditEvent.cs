// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Events.Audit;

namespace NCode.Identity.OpenId.Authentication.Auditing;

/// <summary>
/// An audit event raised when an end user changes, or fails to change, the password of their account on the
/// self-service surface. The <see cref="IAuditEvent.Outcome"/> distinguishes a change from a refusal; neither the
/// current nor the new password is ever recorded.
/// </summary>
// See ADR-1533.
[PublicAPI]
public sealed record PasswordChangeAuditEvent : AuditEvent
{
    /// <inheritdoc />
    public override string EventName => OpenIdAuditEventNames.PasswordChange;

    /// <inheritdoc />
    public override string EventCategory => AuditEventCategories.Authentication;

    /// <summary>
    /// Gets the identifier of the local account whose password changed or was refused.
    /// </summary>
    public string? LocalAccountId { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Lists the local account as the event's only resource, when it is known.
    /// </remarks>
    public override IReadOnlyList<AuditResource> Resources =>
        LocalAccountId is null
            ? []
            : [new AuditResource(AuditResourceTypes.LocalAccount, LocalAccountId)];
}
