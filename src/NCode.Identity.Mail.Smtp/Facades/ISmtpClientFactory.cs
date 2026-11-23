// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using MailKit.Net.Smtp;

namespace NCode.Identity.Mail.Smtp.Facades;

/// <summary>
/// Creates the SMTP client one delivery uses, so the code that talks to the server can be tested without one.
/// </summary>
// See ADR-1532.
internal interface ISmtpClientFactory
{
    /// <summary>
    /// Creates a new, unconnected SMTP client, which the caller disposes.
    /// </summary>
    /// <returns>The new <see cref="ISmtpClient"/>.</returns>
    ISmtpClient Create();
}
