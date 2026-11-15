// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Provides the bounds of the values the directory stores, which its schema and its options validation share.
/// </summary>
internal static class DirectoryLimits
{
    /// <summary>
    /// The longest tenant or team identifier the directory stores, in characters.
    /// </summary>
    public const int MaxIdentifierLength = 300;

    /// <summary>
    /// The longest issuer the directory stores, in characters: the same bound as a cell's own issuers.
    /// </summary>
    public const int MaxIssuerLength = 1000;
}
