// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Tenants.Keys;

/// <summary>
/// Decides whether this cell may change a tenant's keys. A cell changes the keys of a tenant it holds the custody of,
/// and leaves the keys of a tenant whose custody is fenced by a move to or from another cell, or that it does not
/// hold, as they are. The decision reads only the cell's own database.
/// </summary>
// See ADR-1404 and ADR-1514.
internal interface ITenantKeyCustodyGuard
{
    /// <summary>
    /// Decides whether this cell may change the keys of the tenant.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing
    /// <see langword="true"/> while the cell holds the tenant and its key custody is not fenced; otherwise,
    /// <see langword="false"/>.</returns>
    ValueTask<bool> MayChangeKeysAsync(string tenantId, CancellationToken cancellationToken);
}
