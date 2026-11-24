// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Contains the names of the connection strategies the family registers.
/// </summary>
// See ADR-1535.
[PublicAPI]
public static class ConnectionStrategyNames
{
    /// <summary>
    /// The strategy of a connection that holds a local-account database behind a named local-account store.
    /// </summary>
    public const string Local = "local";

    /// <summary>
    /// Determines whether a connection's strategy is the <see cref="Local"/> strategy.
    /// </summary>
    /// <param name="strategy">The name of the connection's strategy.</param>
    /// <returns><see langword="true"/> when the strategy is <see cref="Local"/>, compared ordinally.</returns>
    public static bool IsLocal(string? strategy)
    {
        return string.Equals(strategy, Local, StringComparison.Ordinal);
    }
}
