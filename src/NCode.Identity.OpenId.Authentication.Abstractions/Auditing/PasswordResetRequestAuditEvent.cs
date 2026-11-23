// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.Events.Audit;

namespace NCode.Identity.OpenId.Authentication.Auditing;

/// <summary>
/// An audit event raised when an end user asks for a password reset code. The <see cref="IAuditEvent.Outcome"/>
/// distinguishes a code that was sent from a request that was refused; neither the code nor the account's address is
/// ever recorded.
/// </summary>
// See ADR-1533.
[PublicAPI]
public sealed record PasswordResetRequestAuditEvent : AuditEvent, ISupportAuditDetails
{
    /// <inheritdoc />
    public override string EventName => OpenIdAuditEventNames.PasswordResetRequest;

    /// <inheritdoc />
    public override string EventCategory => AuditEventCategories.Authentication;

    /// <summary>
    /// Gets the identifier of the local account the code was requested for, or <see langword="null"/> when the user
    /// name named no account.
    /// </summary>
    public string? LocalAccountId { get; init; }

    /// <summary>
    /// Gets the unverified user name a refused request presented, or <see langword="null"/> when the request names an
    /// account. It is kept out of <see cref="IAuditEvent.SubjectId"/>, which holds only verified subject identifiers.
    /// </summary>
    public string? AttemptedUsername { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Lists the local account as the event's only resource, when it is known.
    /// </remarks>
    public override IReadOnlyList<AuditResource> Resources =>
        LocalAccountId is null
            ? []
            : [new AuditResource(AuditResourceTypes.LocalAccount, LocalAccountId)];

    /// <inheritdoc />
    public bool HasAuditDetails => AttemptedUsername is not null;

    /// <inheritdoc />
    /// <remarks>
    /// Writes the <c>attempted_username</c> when one was presented; never a credential.
    /// </remarks>
    public void WriteAuditDetails(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (AttemptedUsername is not null)
            writer.WriteString("attempted_username", AttemptedUsername);
    }
}
