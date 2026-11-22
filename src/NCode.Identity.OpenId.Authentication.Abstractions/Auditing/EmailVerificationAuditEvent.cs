// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Events.Audit;

namespace NCode.Identity.OpenId.Authentication.Auditing;

/// <summary>
/// An audit event raised when an end user verifies, or fails to verify, their account's email address with a code. The
/// <see cref="IAuditEvent.Outcome"/> distinguishes a verification from a refusal; neither the code nor the address is
/// ever recorded.
/// </summary>
// See ADR-1533.
[PublicAPI]
public sealed record EmailVerificationAuditEvent : AuditEvent
{
    /// <inheritdoc />
    public override string EventName => OpenIdAuditEventNames.EmailVerification;

    /// <inheritdoc />
    public override string EventCategory => AuditEventCategories.Authentication;

    /// <summary>
    /// Gets the identifier of the local account whose address was verified or refused.
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
