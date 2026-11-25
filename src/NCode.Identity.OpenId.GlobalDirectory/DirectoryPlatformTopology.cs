// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Tenants;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the <see cref="IPlatformTopology"/> of a platform with a global directory. A cell that is not the
/// control-plane cell has a management tenant when it serves a dynamic tenant-resolution strategy.
/// </summary>
internal sealed class DirectoryPlatformTopology(
    IOptions<CellOptions> cellOptions,
    IOptions<TenantResolutionOptions> tenantResolutionOptions
) : IPlatformTopology
{
    /// <inheritdoc />
    public bool HasDirectory => true;

    /// <inheritdoc />
    public bool IsControlPlaneCell => cellOptions.Value.IsControlPlane;

    /// <inheritdoc />
    public bool HasManagementTenant
    {
        get
        {
            if (IsControlPlaneCell)
                return false;

            var strategyCode = tenantResolutionOptions.Value.StrategyCode;
            return strategyCode
                is OpenIdConstants.TenantStrategyCodes.DynamicByHost
                    or OpenIdConstants.TenantStrategyCodes.DynamicByPath;
        }
    }
}
