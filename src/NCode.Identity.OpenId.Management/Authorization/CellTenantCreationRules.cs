// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
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
    /// Determines whether a team may grant a cell the creation of its tenants: its own settings document, or the one of
    /// a team above it, names the cell in <c>tenant_creation_cells</c>. It reads only the documents the teams hold, so
    /// a listing narrows every team to these few before <see cref="GrantsAsync"/> resolves each one's effective
    /// settings.
    /// </summary>
    /// <param name="team">The team, as the cell mirrors it.</param>
    /// <param name="teamsById">Every team the cell mirrors, by identifier, compared without case.</param>
    /// <param name="cellName">The name of the cell.</param>
    /// <returns><see langword="true"/> when the team or a team above it names the cell.</returns>
    public static bool MayGrant(
        PersistedTeam team,
        IReadOnlyDictionary<string, PersistedTeam> teamsById,
        string cellName
    )
    {
        if (NamesCell(team, cellName))
        {
            return true;
        }

        foreach (var ancestorTeamId in team.AncestorTeamIds)
        {
            if (
                teamsById.TryGetValue(ancestorTeamId, out var ancestor)
                && NamesCell(ancestor, cellName)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool NamesCell(PersistedTeam team, string cellName)
    {
        var settings = team.Settings.Value;
        if (
            settings.ValueKind != JsonValueKind.Object
            || !settings.TryGetProperty(OpenIdSettingNames.TenantCreationCells, out var cells)
            || cells.ValueKind != JsonValueKind.Array
        )
        {
            return false;
        }

        foreach (var cell in cells.EnumerateArray())
        {
            if (cell.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = cell.GetString();
            if (string.Equals(name, cellName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

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
