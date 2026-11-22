// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Mail.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.Mail</c> package.
/// </summary>
/// <remarks>
/// This package reserves the <c>32000</c>–<c>32999</c> band per the logging conventions. Values are append-only: never
/// renumber or reuse a shipped identifier.
/// </remarks>
internal static class EventIds
{
    private const int Base = 32000;

    public const int MailDeliveryFailed = Base + 1;
}
