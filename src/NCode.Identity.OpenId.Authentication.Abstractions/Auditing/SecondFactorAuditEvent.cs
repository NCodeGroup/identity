// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.Events.Audit;

namespace NCode.Identity.OpenId.Authentication.Auditing;

/// <summary>
/// An audit event raised when an end user completes, or fails, a second factor at sign-in. The
/// <see cref="IAuditEvent.Outcome"/> distinguishes a completed factor from a refused one; neither the code nor the
/// account's address is ever recorded.
/// </summary>
// See ADR-1533.
[PublicAPI]
public sealed record SecondFactorAuditEvent : AuditEvent, ISupportAuditDetails
{
    /// <inheritdoc />
    public override string EventName => OpenIdAuditEventNames.SecondFactor;

    /// <inheritdoc />
    public override string EventCategory => AuditEventCategories.Authentication;

    /// <summary>
    /// Gets the identifier of the local account the second factor was proved for, or <see langword="null"/> when the
    /// factor was not proved for a local account.
    /// </summary>
    public string? LocalAccountId { get; init; }

    /// <summary>
    /// Gets the second factor, one of <see cref="AuditCredentialFactors"/> or an extension's own name.
    /// </summary>
    public required string Factor { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Lists the local account as the event's only resource, when it is known.
    /// </remarks>
    public override IReadOnlyList<AuditResource> Resources =>
        LocalAccountId is null
            ? []
            : [new AuditResource(AuditResourceTypes.LocalAccount, LocalAccountId)];

    /// <inheritdoc />
    public bool HasAuditDetails => true;

    /// <inheritdoc />
    /// <remarks>
    /// Writes the <c>factor</c>; never a code.
    /// </remarks>
    public void WriteAuditDetails(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteString("factor", Factor);
    }
}
