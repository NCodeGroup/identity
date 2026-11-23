// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Mail.Smtp.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.Mail.Smtp</c> package.
/// </summary>
/// <remarks>
/// This package reserves the <c>33000</c>–<c>33999</c> band per the logging conventions. Values are append-only: never
/// renumber or reuse a shipped identifier.
/// </remarks>
internal static class EventIds
{
    private const int Base = 33000;

    /// <summary>
    /// Identifies the warning that the SMTP server accepted a message but closing the connection failed.
    /// </summary>
    public const int SmtpQuitFailed = Base + 1;
}
