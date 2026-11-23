// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Contains the sign-in methods the family knows. The vocabulary is open.
/// </summary>
// See ADR-1535.
[PublicAPI]
public static class ConnectionSignInMethods
{
    /// <summary>
    /// The server verifies a password: the interactive sign-in form and the password grant serve the connection.
    /// </summary>
    public const string Password = "password";
}
