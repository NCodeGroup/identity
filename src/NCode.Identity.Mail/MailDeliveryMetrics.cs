// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;

namespace NCode.Identity.Mail;

/// <summary>
/// Owns the instruments of the <see cref="MailMetrics.MeterName"/> meter, created once through the
/// <see cref="IMeterFactory"/> so each service provider has its own; the mail job records through its methods and never
/// touches the meter directly.
/// </summary>
/// <param name="meterFactory">The factory that creates the <see cref="MailMetrics.MeterName"/> meter.</param>
// See ADR-1532.
internal sealed class MailDeliveryMetrics(IMeterFactory meterFactory)
{
    internal const string MessagesName = "ncode.identity.mail.messages";

    internal const string ResultAttribute = "ncode.identity.mail.result";

    internal const string DeliveredResult = "delivered";

    internal const string FailedResult = "failed";

    private Counter<long> Messages { get; } =
        meterFactory
            .Create(MailMetrics.MeterName)
            .CreateCounter<long>(
                MessagesName,
                unit: "{message}",
                description: "The number of mail messages a transport delivered or failed to deliver, by result."
            );

    /// <summary>
    /// Counts a message that the transport delivered.
    /// </summary>
    public void Delivered()
    {
        var tag = new KeyValuePair<string, object?>(ResultAttribute, DeliveredResult);
        Messages.Add(1, tag);
    }

    /// <summary>
    /// Counts a message that the transport failed to deliver.
    /// </summary>
    public void Failed()
    {
        var tag = new KeyValuePair<string, object?>(ResultAttribute, FailedResult);
        Messages.Add(1, tag);
    }
}
