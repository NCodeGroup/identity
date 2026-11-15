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

    /// <summary>
    /// The deepest a team is nested, which sizes the column that holds a team path: the same bound as the cell's team
    /// tree.
    /// </summary>
    public const int MaxTeamDepth = 8;

    /// <summary>
    /// The longest team path the directory stores, in characters: <see cref="MaxTeamDepth"/> identifiers of at most
    /// <see cref="MaxIdentifierLength"/> characters, each followed by a slash, after a leading slash.
    /// </summary>
    public const int MaxTeamPathLength = MaxTeamDepth * (MaxIdentifierLength + 1) + 1;
}
