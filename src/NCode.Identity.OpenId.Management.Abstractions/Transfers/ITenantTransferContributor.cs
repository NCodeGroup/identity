// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Transfers;

/// <summary>
/// Contributes one section of a tenant's export and imports it again at the cell that receives the tenant. The
/// contributors are a strategy collection that runs in <see cref="Order"/>.
/// </summary>
// See ADR-1001.
// See ADR-1514.
[PublicAPI]
public interface ITenantTransferContributor
{
    /// <summary>
    /// Gets the name that identifies the contributor's section in an export.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the position of the contributor among the others. A contributor with a lower value runs first when a
    /// tenant is imported and last when its rows are removed. See <see cref="TenantTransferOrders"/> for the built-in
    /// contributors' positions.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Reads the contributor's records of a tenant.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to export.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="TenantTransferSection"/> that holds the records.</returns>
    ValueTask<TenantTransferSection> ExportAsync(
        string tenantId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Writes the records of a section into the tenant.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to import.</param>
    /// <param name="section">The <see cref="TenantTransferSection"/> that the contributor exported.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="TenantTransferImportResult"/> that names the remote keys the imported secrets reference.</returns>
    ValueTask<TenantTransferImportResult> ImportAsync(
        string tenantId,
        TenantTransferSection section,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes the contributor's records of a tenant from the cell that the tenant left.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant that left the cell.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RemoveAsync(string tenantId, CancellationToken cancellationToken);
}
