// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Contains the rule that a tenant limit is read by: the <c>max_tenants</c> setting of a stored platform or team
/// settings document, and a team's limit, the lesser of the platform's and the team's own. The global directory and a
/// cell's local directory both read stored settings through it, so a reservation is judged alike wherever it is made.
/// </summary>
// See ADR-1411.
[PublicAPI]
public static class TenantLimitRules
{
    /// <summary>
    /// The name of the setting that bounds the tenants of a team and of every team beneath it, on a team or the
    /// platform.
    /// </summary>
    // The value of OpenIdSettingNames.MaxTenants, which the directory abstractions do not reference.
    public const string MaxTenantsSettingName = "max_tenants";

    /// <summary>
    /// Reads the <c>max_tenants</c> setting of a stored settings document.
    /// </summary>
    /// <param name="settings">The settings document.</param>
    /// <param name="maxTenants">
    /// The limit, or <see langword="null"/> when the document has no <c>max_tenants</c> setting.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the document is a JSON object whose setting is absent or a non-negative integer;
    /// otherwise, <see langword="false"/> because the document is not a JSON object or the setting cannot be read.
    /// </returns>
    public static bool TryReadMaxTenants(JsonElement settings, out int? maxTenants)
    {
        maxTenants = null;

        // Every stored settings document is a JSON object, the empty object when it holds no settings, so settings of
        // any other kind, null included, can name no limit: they are unreadable, never unlimited.
        if (settings.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!settings.TryGetProperty(MaxTenantsSettingName, out var element))
        {
            return true;
        }

        if (
            element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var limit)
            || limit < 0
        )
        {
            return false;
        }

        maxTenants = limit;
        return true;
    }

    /// <summary>
    /// Gets a team's limit: the lesser of the platform's limit and the team's own, where a limit that is not set is
    /// unlimited.
    /// </summary>
    /// <param name="platformLimit">The platform's limit, or <see langword="null"/> when it sets none.</param>
    /// <param name="teamLimit">The team's own limit, or <see langword="null"/> when it sets none.</param>
    /// <returns>The team's limit, or <see langword="null"/> when neither sets one.</returns>
    public static int? GetEffectiveLimit(int? platformLimit, int? teamLimit) =>
        teamLimit is { } team
            ? platformLimit is { } platform
                ? Math.Min(platform, team)
                : team
            : platformLimit;
}
