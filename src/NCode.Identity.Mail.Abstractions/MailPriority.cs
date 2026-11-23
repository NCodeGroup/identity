// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Specifies how urgently a mail message is to be delivered, as RFC 2156 defines.
/// </summary>
// See ADR-1532.
// A closed vocabulary (ADR-1150): RFC 2156 defines the only values.
[PublicAPI]
public enum MailPriority
{
    /// <summary>
    /// The message is delivered as usual.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// The message may be delivered after other mail.
    /// </summary>
    NonUrgent = 1,

    /// <summary>
    /// The message is delivered ahead of other mail.
    /// </summary>
    Urgent = 2,
}
