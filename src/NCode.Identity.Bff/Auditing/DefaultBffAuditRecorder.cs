// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using NCode.Identity.Bff.Frontends;
using NCode.Identity.Events;
using NCode.Identity.Events.Audit;

namespace NCode.Identity.Bff.Auditing;

/// <summary>
/// Provides the default implementation of <see cref="IBffAuditRecorder"/> that publishes a
/// <see cref="BffAuditEvent"/> through the event seam.
/// </summary>
internal class DefaultBffAuditRecorder(
    IEventPublisher publisher,
    BffFrontendCatalog frontends,
    TimeProvider timeProvider
) : IBffAuditRecorder
{
    private IEventPublisher Publisher { get; } = publisher;
    private BffFrontendCatalog Frontends { get; } = frontends;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    public ValueTask LoginSucceededAsync(
        HttpContext httpContext,
        string subject,
        CancellationToken cancellationToken
    )
    {
        var actor = AuditActor.EndUser(subject);

        return PublishAsync(
            BffAuditEventNames.LoginSucceeded,
            AuditEventCategories.Authentication,
            AuditOutcome.Success,
            actor,
            httpContext,
            subject,
            null,
            null,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask LoginFailedAsync(
        HttpContext httpContext,
        string reason,
        CancellationToken cancellationToken
    )
    {
        var error = new AuditError(reason);

        return PublishAsync(
            BffAuditEventNames.LoginFailed,
            AuditEventCategories.Authentication,
            AuditOutcome.Failure,
            AuditActor.Anonymous,
            httpContext,
            null,
            null,
            error,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask SessionEndedAsync(
        HttpContext? httpContext,
        string subject,
        string reason,
        CancellationToken cancellationToken
    )
    {
        var actor = string.Equals(reason, BffSessionEndReasons.LoggedOut, StringComparison.Ordinal)
            ? AuditActor.EndUser(subject)
            : AuditActor.System;

        return PublishAsync(
            BffAuditEventNames.SessionEnded,
            AuditEventCategories.Session,
            AuditOutcome.Success,
            actor,
            httpContext,
            subject,
            reason,
            null,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask TokenRefreshFailedAsync(
        HttpContext? httpContext,
        string subject,
        string reason,
        CancellationToken cancellationToken
    )
    {
        var actor = AuditActor.EndUser(subject);
        var error = new AuditError(reason);

        return PublishAsync(
            BffAuditEventNames.TokenRefreshFailed,
            AuditEventCategories.Token,
            AuditOutcome.Failure,
            actor,
            httpContext,
            subject,
            null,
            error,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask BackchannelLogoutReceivedAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    ) =>
        PublishAsync(
            BffAuditEventNames.BackchannelLogoutReceived,
            AuditEventCategories.Session,
            AuditOutcome.Success,
            AuditActor.System,
            httpContext,
            null,
            null,
            null,
            cancellationToken
        );

    /// <inheritdoc />
    public ValueTask BackchannelLogoutRejectedAsync(
        HttpContext httpContext,
        string reason,
        CancellationToken cancellationToken
    )
    {
        var error = new AuditError(reason);

        return PublishAsync(
            BffAuditEventNames.BackchannelLogoutRejected,
            AuditEventCategories.Session,
            AuditOutcome.Failure,
            AuditActor.Anonymous,
            httpContext,
            null,
            null,
            error,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask AntiforgeryRejectedAsync(
        HttpContext httpContext,
        string reason,
        CancellationToken cancellationToken
    )
    {
        var error = new AuditError(reason);

        return PublishAsync(
            BffAuditEventNames.AntiforgeryRejected,
            AuditEventCategories.Protocol,
            AuditOutcome.Denied,
            AuditActor.Anonymous,
            httpContext,
            null,
            null,
            error,
            cancellationToken
        );
    }

    private ValueTask PublishAsync(
        string name,
        string category,
        string outcome,
        AuditActor actor,
        HttpContext? httpContext,
        string? subject,
        string? reason,
        AuditError? error,
        CancellationToken cancellationToken
    )
    {
        var now = TimeProvider.GetUtcNow();
        var request = httpContext is null ? null : BffAuditRequestFactory.Create(httpContext);
        var auditEvent = new BffAuditEvent
        {
            EventId = Guid.CreateVersion7(now),
            EventTime = now,
            EventSource = BffAuditEventNames.Source,
            Name = name,
            Category = category,
            ClientId = httpContext is null
                ? null
                : Frontends.Resolve(httpContext)?.Options.ClientId,
            SubjectId = subject,
            Actor = actor,
            Request = request,
            Outcome = outcome,
            Reason = reason,
            Error = error,
        };

        return Publisher.PublishAsync(auditEvent, cancellationToken);
    }
}
