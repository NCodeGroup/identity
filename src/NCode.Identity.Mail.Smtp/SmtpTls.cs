// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail.Smtp;

/// <summary>
/// Specifies how the connection to the SMTP server is protected.
/// </summary>
// See ADR-1532.
// A closed vocabulary (ADR-1150): RFC 3207 and RFC 8314 define the only ways message submission is protected.
[PublicAPI]
public enum SmtpTls
{
    /// <summary>
    /// The connection upgrades with STARTTLS, which the server must offer; the default, for port 587.
    /// </summary>
    StartTls = 0,

    /// <summary>
    /// The connection is TLS from its start, for port 465.
    /// </summary>
    ImplicitTls = 1,

    /// <summary>
    /// The connection is never encrypted. Only a host with <c>LocalDevelopment:Enabled</c> uses it, for a local mail
    /// catcher.
    /// </summary>
    None = 2,
}
