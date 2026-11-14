// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.OpenId.GlobalDirectory</c> package.
/// </summary>
/// <remarks>
/// This package reserves the <c>31000</c>–<c>31999</c> band per the logging conventions. Values are append-only:
/// never renumber or reuse a shipped identifier. Each constant is named exactly as the <c>Log</c> method it identifies.
/// </remarks>
internal static class EventIds
{
    private const int Base = 31000;

    public const int CellRegistrationFailed = Base + 1;
    public const int DirectoryChangeFeedUnavailable = Base + 2;
    public const int DirectoryChangeFeedPassFailed = Base + 3;
}
