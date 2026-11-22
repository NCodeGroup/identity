// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Events.Audit;

namespace NCode.Identity.OpenId.Authentication.Auditing;

/// <summary>
/// An audit event raised when an end user resets, or fails to reset, a forgotten password with a code. The
/// <see cref="IAuditEvent.Outcome"/> distinguishes a reset from a refusal; neither the code nor the new password is
/// ever recorded.
/// </summary>
// See ADR-1533.
[PublicAPI]
public sealed record PasswordResetAuditEvent : AuditEvent
{
    /// <inheritdoc />
    public override string EventName => OpenIdAuditEventNames.PasswordReset;

    /// <inheritdoc />
    public override string EventCategory => AuditEventCategories.Authentication;

    /// <summary>
    /// Gets the identifier of the local account whose password was reset or refused, or <see langword="null"/> when the
    /// code named no account.
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
