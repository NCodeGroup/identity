// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using NCode.Identity.Dispatch;
using NCode.Identity.Mail.Logging;

namespace NCode.Identity.Mail;

/// <summary>
/// Executes a <see cref="MailDeliveryJob"/> by handing its message to the <see cref="IMailTransport"/>. A delivery
/// failure is counted and logged without the recipient or the body, then rethrown so the <see cref="IJobDispatcher"/>
/// retries the job.
/// </summary>
// See ADR-1532.
internal sealed class MailDeliveryJobHandler(
    IMailTransport transport,
    MailDeliveryMetrics metrics,
    ILogger<MailDeliveryJobHandler> logger
) : IDispatchJobHandler<MailDeliveryJob>
{
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
            await Transport.DeliverAsync(job.Message, cancellationToken);
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
