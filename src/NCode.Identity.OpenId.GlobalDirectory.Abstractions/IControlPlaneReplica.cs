// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Reads the replica of the control plane's authority and management keys that the cell holds in its own database. A
/// read never calls the directory or another cell. Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface IControlPlaneReplica
{
    /// <summary>
    /// Gets the public keys that verify management tokens.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the keys
    /// that are not expired and not revoked.</returns>
    ValueTask<ReplicatedManagementKeys> GetManagementKeysAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the platform and team roles that a principal holds according to the replicated authority documents.
    /// </summary>
    /// <param name="principalId">The control-plane principal identifier.</param>
    /// <param name="teamId">The team whose roles are read together with those of every team above it, or <c>null</c> to
    /// read no team roles.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the
    /// authority.</returns>
    ValueTask<ReplicatedAuthority> GetAuthorityAsync(
        string principalId,
        string? teamId,
        CancellationToken cancellationToken
    );
}
