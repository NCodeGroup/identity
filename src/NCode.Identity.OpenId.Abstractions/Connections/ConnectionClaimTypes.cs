// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Contains the server-owned claim types that name a connection.
/// </summary>
// See ADR-1535.
[PublicAPI]
public static class ConnectionClaimTypes
{
    /// <summary>
    /// The claim type that holds the identifier of the connection a subject signed in through. The server stamps it and
    /// never copies an upstream claim of this type.
    /// </summary>
    public const string Connection = "urn:ncode:identity:connection";
}
