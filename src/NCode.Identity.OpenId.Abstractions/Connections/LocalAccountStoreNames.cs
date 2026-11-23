// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Contains the names of local-account stores.
/// </summary>
// See ADR-1535.
[PublicAPI]
public static class LocalAccountStoreNames
{
    /// <summary>
    /// The name a store registers under unless it is given another.
    /// </summary>
    public const string Default = "default";
}
