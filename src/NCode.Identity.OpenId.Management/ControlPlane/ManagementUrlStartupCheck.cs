// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Tenants;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Stops the host as it starts when the platform has a directory, the cell is the control-plane cell or has a
/// management tenant, and the origin of <c>Cell:ManagementUrl</c> differs from the origin of the root tenant's issuer,
/// because a DPoP proof for the management API names the URL the cell registers, and the tenant that issues the
/// management tokens answers at that origin.
/// </summary>
/// <param name="platformTopology">The shape of the platform the cell belongs to.</param>
/// <param name="cellOptions">The options of the cell, which name its management URL.</param>
/// <param name="tenantResolver">The resolver that names the root tenant and derives its issuer.</param>
// See ADR-1536.
internal sealed class ManagementUrlStartupCheck(
    IPlatformTopology platformTopology,
    IOptions<CellOptions> cellOptions,
    ITenantResolver tenantResolver
) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (
            !platformTopology.HasDirectory
            || !(platformTopology.IsControlPlaneCell || platformTopology.HasManagementTenant)
        )
        {
            return;
        }

        var managementUrl = cellOptions.Value.ManagementUrl;
        if (!Uri.TryCreate(managementUrl, UriKind.Absolute, out var managementUri))
        {
            throw new InvalidOperationException(
                $"{CellOptions.SectionName}:{nameof(CellOptions.ManagementUrl)} must be an absolute URL when the platform has a directory."
            );
        }

        var rootTenantId = tenantResolver.GetControlPlaneTenantId();
        var issuer = await tenantResolver.GetDefaultIssuerAsync(rootTenantId, cancellationToken);
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri))
        {
            throw new InvalidOperationException(
                $"The root tenant '{rootTenantId}' has no issuer, so {CellOptions.SectionName}:{nameof(CellOptions.ManagementUrl)} cannot be checked against its origin."
            );
        }

        var managementOrigin = managementUri.GetLeftPart(UriPartial.Authority);
        var issuerOrigin = issuerUri.GetLeftPart(UriPartial.Authority);
        if (!string.Equals(managementOrigin, issuerOrigin, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{CellOptions.SectionName}:{nameof(CellOptions.ManagementUrl)} '{managementOrigin}' must have the origin of the root tenant's issuer '{issuerOrigin}' when the platform has a directory and the cell is the control-plane cell or has a management tenant."
            );
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
