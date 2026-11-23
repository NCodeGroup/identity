// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Contains the defaults of connections.
/// </summary>
// See ADR-1535.
[PublicAPI]
public static class ConnectionDefaults
{
    /// <summary>
    /// The name of the connection that every tenant is created with, and the default of <c>default_connection</c>.
    /// </summary>
    public const string Name = "local";
}
