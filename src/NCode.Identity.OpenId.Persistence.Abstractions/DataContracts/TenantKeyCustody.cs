// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains the values of a tenant's key-custody fence, which says whether this cell may use the tenant's keys while a
/// move to or from another cell is in flight. The set is closed: a move has two sides, and the cell holding the keys is
/// the third state.
/// </summary>
[PublicAPI]
public static class TenantKeyCustody
{
    /// <summary>
    /// The cell holds the tenant's keys.
    /// </summary>
    public const string Held = "held";

    /// <summary>
    /// The tenant's keys are leaving this cell for another one.
    /// </summary>
    public const string Outgoing = "outgoing";

    /// <summary>
    /// The tenant's keys arrive in this cell from another one, and the directory has not yet confirmed the move.
    /// </summary>
    public const string Incoming = "incoming";

    /// <summary>
    /// Determines whether a custody value is <see cref="Held"/>, which is the only value under which this cell may
    /// change the tenant's keys.
    /// </summary>
    /// <param name="custody">The tenant's key custody.</param>
    /// <returns><c>true</c> when the cell holds the tenant's keys; otherwise, <c>false</c>.</returns>
    public static bool IsHeld(string custody) =>
        string.Equals(custody, Held, StringComparison.Ordinal);
}
