// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Teams;

/// <summary>
/// Represents the REST resource that lists the root keys a team brings, with the key wrappers it may choose and the
/// access binding it can require of the server.
/// </summary>
// See ADR-1411 and ADR-1414.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class TeamRootKeysResource
{
    /// <summary>
    /// Gets the root keys the team brings, ordered by region.
    /// </summary>
    public required List<TeamRootKeyResource> RootKeys { get; init; }

    /// <summary>
    /// Gets the names of the key wrappers that a team may choose for its root keys. It is empty when the deployment
    /// lets no team bring a key.
    /// </summary>
    public required List<string> RootKeyProviders { get; init; }

    /// <summary>
    /// Gets the value that an identity the team grants can require of the server, such as the external ID of an AWS
    /// role's trust policy.
    /// </summary>
    public required string RootKeyAccessBinding { get; init; }
}
