// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Logging;

/// <summary>
/// Defines the reserved logging event IDs for the <c>NCode.Identity.OpenId.GlobalDirectory.Server</c> package.
/// </summary>
/// <remarks>
/// This package reserves the <c>37000</c>–<c>37999</c> band per the logging conventions. Values are append-only: never
/// renumber or reuse a shipped identifier. Each constant is named exactly as the <c>Log</c> method it identifies.
/// </remarks>
internal static class EventIds
{
    private const int Base = 37000;

    /// <summary>
    /// Identifies the information that the token endpoint refused a cell's client assertion; the caller is told only
    /// that the assertion or the cell is refused.
    /// </summary>
    public const int ClientAssertionRefused = Base + 1;
}
