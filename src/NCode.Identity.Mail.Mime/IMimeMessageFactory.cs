// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using MimeKit;

namespace NCode.Identity.Mail.Mime;

/// <summary>
/// Builds the MIME form of a <see cref="MailMessage"/> for a transport that sends MIME.
/// </summary>
/// <remarks>
/// The MIME message reads each body, attachment and linked resource through <see cref="MailContent"/>, so the message
/// that holds the content must outlive the MIME message and is the one the caller disposes.
/// </remarks>
// See ADR-1532.
[PublicAPI]
public interface IMimeMessageFactory
{
    /// <summary>
    /// Creates the options that write a MIME message, which hide the <c>Bcc</c> header so that a delivered message does
    /// not show its blind copies.
    /// </summary>
    /// <returns>The <see cref="FormatOptions"/> to write a message with.</returns>
    FormatOptions CreateFormatOptions();

    /// <summary>
    /// Creates the MIME form of a mail message.
    /// </summary>
    /// <param name="message">The message to build the MIME form of, which the caller owns and disposes.</param>
    /// <param name="defaultFrom">
    /// The address the message is from when <see cref="MailMessage.From"/> is <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that completes with the MIME message, which the caller disposes.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the message has no recipient or no body, names a reserved header, holds a linked resource with no
    /// content identifier, or holds content with an unknown transfer encoding.
    /// </exception>
    ValueTask<MimeMessage> CreateAsync(
        MailMessage message,
        MailAddress defaultFrom,
        CancellationToken cancellationToken
    );
}
