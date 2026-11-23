// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Mail;

/// <summary>
/// Provides the <see cref="IMailTransport"/> that is registered until a host registers a transport: it refuses every
/// message, so each delivery fails and is logged.
/// </summary>
// See ADR-1532.
internal sealed class UnconfiguredMailTransport : IMailTransport
{
    /// <inheritdoc />
    public ValueTask DeliverAsync(MailMessage message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "No mail transport is registered, so the message is not sent. Register one, such as AddSmtpMailTransport with a 'Mail:Smtp' section."
        );
}
