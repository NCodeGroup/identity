// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence;

namespace NCode.Identity.OpenId.Management.Authorization;

/// <summary>
/// The team node that a tenant's creation is authorized at: the team that will hold the tenant. It is the node every
/// other creation beneath a team is judged at, and it also names the creation of a tenant, so
/// <see cref="CellTenantCreationHandler"/>, which lets a cell's <c>CellAdmin</c> create the tenants of a team that
/// grants the cell, judges this creation and no other operation at the team.
/// </summary>
// See ADR-1536 (decision 10).
internal sealed class TenantCreationNode : IResourceNode
{
    /// <inheritdoc cref="ISupportTenantId.TenantId"/>
    public required string TenantId { get; init; }

    /// <inheritdoc />
    public string ResourceType => ResourceNodeTypes.Team;

    /// <inheritdoc />
    public required string ResourceId { get; init; }

    /// <summary>
    /// Creates the node of the creation of a tenant in a team.
    /// </summary>
    /// <param name="managementTenantId">The identifier of the tenant whose management API serves the request.</param>
    /// <param name="teamId">The identifier of the team that will hold the tenant.</param>
    /// <returns>The node.</returns>
    public static TenantCreationNode For(string managementTenantId, string teamId) =>
        new() { TenantId = managementTenantId, ResourceId = teamId };
}
