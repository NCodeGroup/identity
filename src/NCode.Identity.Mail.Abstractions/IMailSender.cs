// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Sends mail on behalf of the server's features: it accepts a message for delivery out of band, retrying a delivery
/// that fails, and returns once the message is queued, so the caller never waits on the mail provider.
/// </summary>
// See ADR-1532.
[PublicAPI]
public interface IMailSender
{
    /// <summary>
    /// Queues <paramref name="message"/> for delivery.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that completes once the message is queued.</returns>
    ValueTask SendAsync(MailMessage message, CancellationToken cancellationToken);
}
