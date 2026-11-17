// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Tenants;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Provides the bounds of the values the directory stores, which its schema and its options validation share, and the
/// bounds of the documents it publishes.
/// </summary>
internal static class DirectoryLimits
{
    /// <summary>
    /// The longest tenant or team identifier the directory stores, in characters: the longest identifier the cells'
    /// identifier rule accepts, <see cref="TenantIdentifierRules.MaxTenantIdLength"/>.
    /// </summary>
    public const int MaxIdentifierLength = TenantIdentifierRules.MaxTenantIdLength;

    /// <summary>
    /// The longest issuer the directory stores, in characters: the bound of a cell's own issuers,
    /// <see cref="OpenIdMaxLengths.Issuer"/>.
    /// </summary>
    public const int MaxIssuerLength = OpenIdMaxLengths.Issuer;

    /// <summary>
    /// The deepest a team is nested, which sizes the column that holds a team path: the depth of a cell's team tree,
    /// <see cref="TeamPaths.MaxDepth"/>.
    /// </summary>
    public const int MaxTeamDepth = TeamPaths.MaxDepth;

    /// <summary>
    /// The longest team path the directory stores, in characters: <see cref="MaxTeamDepth"/> identifiers of at most
    /// <see cref="MaxIdentifierLength"/> characters, each followed by a slash, after a leading slash.
    /// </summary>
    public const int MaxTeamPathLength = MaxTeamDepth * (MaxIdentifierLength + 1) + 1;

    /// <summary>
    /// The most characters a cell's kind holds.
    /// </summary>
    public const int MaxCellKindLength = 32;

    /// <summary>
    /// The most characters a cell's management URL holds.
    /// </summary>
    public const int MaxManagementUrlLength = 2000;

    /// <summary>
    /// The most bytes that the JSON of a published team or platform document holds, so a page of the change feed holds
    /// whole documents within the response a cell reads.
    /// </summary>
    public const int MaxDocumentBytes = 64 * 1024;
}
