// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Tenants.Keys;

/// <summary>
/// Decides whether this cell may change a tenant's keys. A cell changes the keys of a tenant the global directory
/// places in this cell or in no cell, and leaves the keys of a tenant the directory places in another cell, or whose
/// placement it cannot read, as they are.
/// </summary>
// See ADR-1404 and ADR-1514.
internal interface ITenantKeyPlacementGuard
{
    /// <summary>
    /// Decides whether this cell may change the keys of the tenant.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing
    /// <see langword="true"/> while the global directory places the tenant in this cell or in no cell; otherwise,
    /// <see langword="false"/>.</returns>
    ValueTask<bool> MayChangeKeysAsync(string tenantId, CancellationToken cancellationToken);
}
