// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Connections;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Tests the strategy of a connection that a local account signs in through.
/// </summary>
internal static class LocalAccountSignedInAccounts
{
    /// <summary>
    /// Determines whether the connection's strategy is the <c>local</c> strategy.
    /// </summary>
    /// <param name="connection">The <see cref="ResolvedConnection"/> to test.</param>
    /// <returns><see langword="true"/> when the connection holds local accounts.</returns>
    public static bool IsLocal(ResolvedConnection connection)
    {
        return string.Equals(
            connection.Connection.Strategy,
            ConnectionStrategyNames.Local,
            StringComparison.Ordinal
        );
    }
}
