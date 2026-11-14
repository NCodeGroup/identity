// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Secrets.Keys;

namespace NCode.Identity.OpenId.Management.Transfers;

/// <summary>
/// Issues the one-time keys that seal a tenant's export to the cell that receives the tenant. A key is an
/// <c>EC P-256</c> key for <c>ECDH-ES+A256KW</c>. Its private half is protected, shared by every replica of the cell,
/// expires after one hour and is taken at most once.
/// </summary>
// See ADR-1514.
[PublicAPI]
public interface ITenantTransferKeyService
{
    /// <summary>
    /// Creates a transfer key, stores its protected private half for one hour and returns its public half.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <c>kid</c> and the public <c>JWK</c> of the issued key.</returns>
    ValueTask<TenantTransferKey> CreateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Takes the transfer key named <paramref name="keyId"/>. Of any number of calls for the same key, across every
    /// replica of the cell, at most one returns it.
    /// </summary>
    /// <param name="keyId">The <c>kid</c> of the transfer key.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The private <see cref="SecretKey"/>; or <see langword="null"/> when the key is unknown, expired, already taken or unreadable.</returns>
    ValueTask<SecretKey?> TakeAsync(string keyId, CancellationToken cancellationToken);
}
