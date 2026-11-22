// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Contains the rule that names the team a tenant is reserved in. The platform's own team is a team each cell creates
/// for itself, and the global directory holds no record of it, so a tenant of that team is reserved in no team and no
/// team limit applies to it; every other team is one the control plane publishes. A cell reserves every tenant it
/// creates through it, on demand or through the management API, so a tenant is reserved alike however it is created.
/// </summary>
// See ADR-1411.
[PublicAPI]
public static class TenantReservationRules
{
    /// <summary>
    /// Gets the team a tenant is reserved in, which <see cref="TenantReservation.TeamId"/> carries.
    /// </summary>
    /// <param name="teamId">The identifier of the team that holds the tenant in its cell.</param>
    /// <param name="platformTeamId">
    /// The identifier of the platform's own team, which the tenant-resolution options name.
    /// </param>
    /// <returns>
    /// The identifier of the tenant's team, or <see langword="null"/> when it is the platform's own team.
    /// </returns>
    public static string? GetTeamId(string teamId, string platformTeamId) =>
        string.Equals(teamId, platformTeamId, StringComparison.Ordinal) ? null : teamId;
}
