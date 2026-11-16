// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts.Tenants;

namespace NCode.Identity.OpenId.Management.Promotions;

/// <summary>
/// Defines the contract to export the configuration of one tenant as a <see cref="TenantConfigurationDocument"/>,
/// which a promotion into another tenant of the same region reads in place of the source tenant.
/// </summary>
// See ADR-1425.
internal interface ITenantConfigurationExporter
{
    /// <summary>
    /// Exports a tenant's settings, clients, client grants and resource servers, stamped with the region of the cell.
    /// The export leaves out machine clients, clients the host manages and the grants of those clients, and it holds no
    /// secret.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to export.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The tenant's configuration document, or <see langword="null"/> when the tenant does not exist.
    /// </returns>
    ValueTask<TenantConfigurationDocument?> ExportAsync(
        string tenantId,
        CancellationToken cancellationToken
    );
}
