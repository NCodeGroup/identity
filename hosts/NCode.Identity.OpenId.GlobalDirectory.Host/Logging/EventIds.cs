// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.OpenId.GlobalDirectory.Host</c> host.
/// </summary>
/// <remarks>
/// This host reserves the <c>36000</c>–<c>36999</c> band per the logging conventions. Values are append-only: never
/// renumber or reuse a shipped identifier.
/// </remarks>
internal static class EventIds
{
    private const int Base = 36000;

    /// <summary>
    /// Identifies the error that the migrate command failed, so the directory database may not be migrated.
    /// </summary>
    public const int DirectoryMigrateCommandFailed = Base + 1;
}
