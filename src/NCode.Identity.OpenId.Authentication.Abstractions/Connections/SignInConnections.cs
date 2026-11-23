// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Connections;

namespace NCode.Identity.OpenId.Authentication.Connections;

/// <summary>
/// Contains the connections an interactive sign-in offers and the one it preselects.
/// </summary>
// See ADR-1535.
[PublicAPI]
public sealed class SignInConnections
{
    /// <summary>
    /// Gets the connections the sign-in offers, ordered by name.
    /// </summary>
    public required IReadOnlyList<ResolvedConnection> Offered { get; init; }

    /// <summary>
    /// Gets the offered connection that the sign-in preselects, or <see langword="null"/> when none is preselected.
    /// </summary>
    public ResolvedConnection? Preselected { get; init; }
}
