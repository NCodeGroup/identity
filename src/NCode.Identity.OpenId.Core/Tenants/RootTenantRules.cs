// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.OpenId.Tenants;

/// <summary>
/// Contains the rules of the root tenant, the control-plane tenant the selected strategy names.
/// </summary>
// See ADR-1166 and ADR-1411.
internal static class RootTenantRules
{
    /// <summary>
    /// Determines whether a tenant is the one whose bearer tokens the global directory accepts. That is the
    /// control-plane cell's control-plane tenant, so the directory cannot answer for it before the cell serves.
    /// </summary>
    /// <param name="cellOptions">The options of the cell that the host runs.</param>
    /// <param name="tenantResolver">The resolver that names the control-plane tenant.</param>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <returns>
    /// <c>true</c> when the host runs the control-plane cell and the tenant is its control-plane tenant; otherwise,
    /// <c>false</c>.
    /// </returns>
    public static bool IsDirectoryTokenIssuer(
        CellOptions cellOptions,
        ITenantResolver tenantResolver,
        string tenantId
    )
    {
        if (!cellOptions.IsControlPlane)
            return false;

        var controlPlaneTenantId = tenantResolver.GetControlPlaneTenantId();
        return string.Equals(tenantId, controlPlaneTenantId, StringComparison.Ordinal);
    }
}
