// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using NCode.Identity.Dispatch;
using NCode.Identity.Mail.Logging;

namespace NCode.Identity.Mail;

/// <summary>
/// Executes a <see cref="MailDeliveryJob"/> by composing its model into a message with the registered
/// <see cref="IMailComposer"/>, handing the message to the <see cref="IMailTransport"/> and disposing it. A composition
/// or delivery failure is counted and logged without the recipient or the body, then rethrown so the
/// <see cref="IJobDispatcher"/> retries the job.
/// </summary>
// See ADR-1532.
internal sealed class MailDeliveryJobHandler(
    MailComposerRegistry composerRegistry,
    IMailTransport transport,
    MailDeliveryMetrics metrics,
    ILogger<MailDeliveryJobHandler> logger
) : IDispatchJobHandler<MailDeliveryJob>
{
    private MailComposerRegistry ComposerRegistry { get; } = composerRegistry;
    private IMailTransport Transport { get; } = transport;
    private MailDeliveryMetrics Metrics { get; } = metrics;
    private ILogger Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask HandleAsync(
        MailDeliveryJob job,
        DispatchContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var modelType = job.Model.GetType();
            var composer = ComposerRegistry.Get(modelType);
            await using var message = await composer.ComposeAsync(job.Model, cancellationToken);
            await Transport.DeliverAsync(message, cancellationToken);
            Metrics.Delivered();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            Metrics.Failed();
            Logger.MailDeliveryFailed(exception, context.Attempt, context.MaxAttempts);

            throw;
        }
    }
}
