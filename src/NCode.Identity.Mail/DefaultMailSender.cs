// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Dispatch;

namespace NCode.Identity.Mail;

/// <summary>
/// Provides the default implementation of <see cref="IMailSender"/>, which queues a <see cref="MailDeliveryJob"/>
/// through the <see cref="IJobDispatcher"/> with four attempts that back off from five seconds to one minute, so a
/// one-time code still arrives within its lifetime.
/// </summary>
// See ADR-1532.
internal sealed class DefaultMailSender(IJobDispatcher jobDispatcher) : IMailSender
{
    /// <summary>
    /// Gets the options with which every delivery is dispatched.
    /// </summary>
    internal static DispatchOptions DeliveryOptions { get; } =
        new()
        {
            MaxAttempts = 4,
            InitialBackoff = TimeSpan.FromSeconds(5),
            MaxBackoff = TimeSpan.FromMinutes(1),
        };

    private IJobDispatcher JobDispatcher { get; } = jobDispatcher;

    /// <inheritdoc />
    public ValueTask SendAsync(MailMessage message, CancellationToken cancellationToken)
    {
        var job = new MailDeliveryJob(message);
        return JobDispatcher.DispatchAsync(job, DeliveryOptions, cancellationToken);
    }
}
