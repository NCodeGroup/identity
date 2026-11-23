// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail.Smtp.Options;

/// <summary>
/// Contains the options for the SMTP mail transport, bound from the <see cref="SectionName"/> configuration section.
/// </summary>
// See ADR-1532.
[PublicAPI]
public sealed class SmtpMailTransportOptions
{
    /// <summary>
    /// Gets the name of the configuration section the options are bound from.
    /// </summary>
    public const string SectionName = "Mail:Smtp";

    /// <summary>
    /// Gets or sets the host name of the SMTP server.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port of the SMTP server. Defaults to 587, the message submission port.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Gets or sets how the connection is protected. Defaults to <see cref="SmtpTls.StartTls"/>.
    /// </summary>
    public SmtpTls Tls { get; set; } = SmtpTls.StartTls;

    /// <summary>
    /// Gets or sets the user name the connection authenticates with, or <see langword="null"/> to send without
    /// authenticating. Set together with <see cref="Password"/>.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the password the connection authenticates with. Set together with <see cref="UserName"/>.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the address every message is sent from, which the provider authorizes.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the sender when a message names none, or <see langword="null"/> for none.
    /// </summary>
    public string? FromName { get; set; }

    /// <summary>
    /// Gets or sets how long a connection or a command waits for the server before it fails. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
