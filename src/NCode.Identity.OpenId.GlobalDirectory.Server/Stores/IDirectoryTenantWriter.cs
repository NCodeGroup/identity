// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Stores;

/// <summary>
/// Writes the tenant changes that a cell makes to the directory, authorizing the calling cell against the stored
/// tenant record in the transaction that writes it. A cell is authorized for a tenant when it holds the tenant or is
/// the control-plane cell; for a tenant with no record, only the control-plane cell is authorized.
/// </summary>
// See ADR-1411.
internal interface IDirectoryTenantWriter
{
    /// <summary>
    /// Releases the reservation of a tenant whose creation failed. A tenant with no record has nothing to release, and
    /// a tenant that is not reserved is kept.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="callerCell">The name of the cell that makes the call.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="DirectoryTenantWriteResult"/> that is done, or forbidden.</returns>
    ValueTask<DirectoryTenantWriteResult> ReleaseAsync(
        string tenantId,
        string callerCell,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Retires the identifier of a deleted tenant and the issuer the tenant holds. A tenant with no record is retired
    /// with the issuer the retirement names, and a record that is already retired stays as it is.
    /// </summary>
    /// <param name="retirement">The <see cref="TenantRetirement"/> describing the identifier to retire.</param>
    /// <param name="callerCell">The name of the cell that makes the call.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="DirectoryTenantWriteResult"/> that is done, or forbidden.</returns>
    ValueTask<DirectoryTenantWriteResult> RetireAsync(
        TenantRetirement retirement,
        string callerCell,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Changes the issuer of a tenant and retires the issuer the tenant gives up. The change names the issuer the
    /// tenant holds, and a change to the issuer the tenant already holds is done without a write.
    /// </summary>
    /// <param name="change">The <see cref="TenantIssuerChange"/> describing the previous and the new issuer.</param>
    /// <param name="callerCell">The name of the cell that makes the call.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="DirectoryTenantWriteResult"/> that is done, forbidden, not found or a conflict.</returns>
    ValueTask<DirectoryTenantWriteResult> ChangeIssuerAsync(
        TenantIssuerChange change,
        string callerCell,
        CancellationToken cancellationToken
    );
}
