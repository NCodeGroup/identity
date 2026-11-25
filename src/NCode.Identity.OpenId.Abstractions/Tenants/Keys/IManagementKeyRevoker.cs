// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Tenants.Keys;

/// <summary>
/// Revokes a compromised management key of the control-plane tenant in the control-plane cell: the key leaves the
/// tenant's key set at once, a successor signs in its place, and every cell refuses the key from the management keys
/// document that the change publishes.
/// </summary>
// See ADR-1536.
[PublicAPI]
public interface IManagementKeyRevoker
{
    /// <summary>
    /// Revokes the control-plane tenant's key with the specified key identifier. A pending successor of the same use
    /// activates at once; when none exists, the revoker generates one that is active at once. The key is removed, the
    /// revocation is recorded and the tenant's keys are announced to every replica in one unit of work.
    /// </summary>
    /// <param name="keyId">The key identifier (<c>kid</c>) of the key to revoke.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation, containing the
    /// outcome.</returns>
    ValueTask<ManagementKeyRevocationResult> RevokeAsync(
        string keyId,
        CancellationToken cancellationToken
    );
}
