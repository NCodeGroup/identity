// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Delivers a mail message to a mail provider, such as an SMTP server. It throws when the provider does not accept the
/// message, so the delivery is retried.
/// </summary>
// See ADR-1532.
[PublicAPI]
public interface IMailTransport
{
    /// <summary>
    /// Delivers <paramref name="message"/> to the mail provider.
    /// </summary>
    /// <param name="message">The message to deliver.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that completes once the provider has accepted the message.</returns>
    ValueTask DeliverAsync(MailMessage message, CancellationToken cancellationToken);
}
