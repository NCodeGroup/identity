// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Specifies how important a mail message is to its recipient, as RFC 2156 defines.
/// </summary>
// See ADR-1532.
// A closed vocabulary (ADR-1150): RFC 2156 defines the only values.
[PublicAPI]
public enum MailImportance
{
    /// <summary>
    /// The message has the usual importance.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// The message has a low importance.
    /// </summary>
    Low = 1,

    /// <summary>
    /// The message has a high importance.
    /// </summary>
    High = 2,
}
