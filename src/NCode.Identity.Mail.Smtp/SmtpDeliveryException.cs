// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using MailKit.Net.Smtp;

namespace NCode.Identity.Mail.Smtp;

/// <summary>
/// The exception that is thrown when the SMTP server refuses a message. It carries the numeric status of the refusal
/// and never the server's reply text, which can echo the recipient's address.
/// </summary>
/// <param name="statusCode">The status that the SMTP server replied with.</param>
// See ADR-1532.
internal sealed class SmtpDeliveryException(SmtpStatusCode statusCode)
    : Exception($"The SMTP server refused the message with status {(int)statusCode}.")
{
    /// <summary>
    /// Gets the status that the SMTP server replied with.
    /// </summary>
    public SmtpStatusCode StatusCode { get; } = statusCode;
}
