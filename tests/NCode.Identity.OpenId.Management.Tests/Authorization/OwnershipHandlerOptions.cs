// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.Management;

/// <summary>
/// Provides the optional inputs of an authorization decision that a test asks the ownership handler for.
/// </summary>
internal sealed class OwnershipHandlerOptions
{
    /// <summary>
    /// Gets a value indicating whether the handler asks the control plane, so it opens the request's tenant scope and
    /// reads the node's stores. A caller without a <c>sub</c> or <c>client_id</c> claim is judged before any of that.
    /// </summary>
    public bool Decides { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the control plane answers a principal for the caller, so the handler reads the
    /// cell's authority chain and role assignments unless the control plane already granted the role.
    /// </summary>
    public bool KnowsPrincipal { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the handler reads the tenant, which it does not for a team or platform node.
    /// </summary>
    public bool ReadsTenant { get; init; } = true;

    /// <summary>
    /// Gets the identifier of the team that owns the tenant, or <see langword="null"/> for a tenant that is unknown.
    /// </summary>
    public string? TenantTeamId { get; init; } = OwnershipHandlerTests.TeamId;

    /// <summary>
    /// Gets the identifiers of the ancestors of the team that owns the tenant, or <see langword="null"/> for none.
    /// </summary>
    public string[]? TeamAncestors { get; init; }

    /// <summary>
    /// Gets the identifier of the team that owns the client a client node names, or <see langword="null"/> for a client
    /// that no team owns.
    /// </summary>
    public string? ClientOwningTeamId { get; init; }

    /// <summary>
    /// Gets the answer of the control plane, or <see langword="null"/> to answer from the caller's role assignments.
    /// </summary>
    public Func<ControlPlaneAuthorityRequest, ControlPlaneAuthority>? Answer { get; init; }
}
