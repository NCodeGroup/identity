// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Settings;

namespace NCode.Identity.OpenId.Management.Authorization;

/// <summary>
/// Decides whether a team grants a workload cell the creation of its tenants: the team is enabled, is not the
/// platform's own team, which holds the control-plane tenant and every cell's management tenant, and lists the cell in
/// its effective <c>tenant_creation_cells</c> setting.
/// </summary>
// See ADR-1536 (decision 10).
internal static class CellTenantCreationRules
{
    /// <summary>
    /// Determines whether a team grants a cell the creation of its tenants.
    /// </summary>
    /// <param name="effectiveSettingsResolver">The resolver of the team's effective settings.</param>
    /// <param name="team">The team, as the cell mirrors it.</param>
    /// <param name="cellName">The name of the cell.</param>
    /// <param name="platformTeamId">The identifier of the platform's own team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing
    /// <see langword="true"/> when the team grants the cell.</returns>
    public static async ValueTask<bool> GrantsAsync(
        IEffectiveSettingsResolver effectiveSettingsResolver,
        PersistedTeam team,
        string cellName,
        string platformTeamId,
        CancellationToken cancellationToken
    )
    {
        if (
            team.IsDisabled
            || string.Equals(team.TeamId, platformTeamId, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        var teamSettings = await effectiveSettingsResolver.ResolveTeamAsync(
            team.TeamId,
            cancellationToken
        );
        return teamSettings.TryGetValue(OpenIdSettingKeys.TenantCreationCells, out var cells)
            && cells.Contains(cellName, StringComparer.Ordinal);
    }
}
