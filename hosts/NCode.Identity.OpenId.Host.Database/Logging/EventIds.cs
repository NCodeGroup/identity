// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Host.Database.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.OpenId.Host.Database</c> project.
/// </summary>
/// <remarks>
/// This project reserves the <c>30000</c>–<c>30999</c> band per the logging conventions. Values are append-only: never
/// renumber or reuse a shipped identifier.
/// </remarks>
internal static class EventIds
{
    private const int Base = 30000;

    /// <summary>
    /// Identifies the warning that the runtime database role is not subject to row-level security, which a host
    /// configured for local development logs instead of refusing to start.
    /// </summary>
    public const int RuntimeRolePostureViolated = Base + 1;
}
