// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Principals;

/// <summary>
/// The names of the built-in kinds of principal. The vocabulary is open: a describer may name another kind.
/// </summary>
[PublicAPI]
public static class PrincipalTypes
{
    /// <summary>
    /// A local account.
    /// </summary>
    public const string LocalAccount = "local_account";

    /// <summary>
    /// A client acting as itself.
    /// </summary>
    public const string Client = "client";
}
