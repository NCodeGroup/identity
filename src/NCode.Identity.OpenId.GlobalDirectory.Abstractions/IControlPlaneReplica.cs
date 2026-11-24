// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Secrets.Keys;

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

/// <summary>
/// Describes the management keys that a cell holds in its replica.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record ReplicatedManagementKeys
{
    /// <summary>
    /// Gets a value that indicates whether the replica holds a management keys document with at least one key. A cell
    /// that holds none has not yet received the document and cannot validate a management token.
    /// </summary>
    public required bool HasKeys { get; init; }

    /// <summary>
    /// Gets the public keys that are not expired and not revoked, including keys that activate later.
    /// </summary>
    public required IReadOnlyCollection<SecretKey> Keys { get; init; }

    /// <summary>
    /// Gets the identifiers of the keys that the cell refuses.
    /// </summary>
    public required IReadOnlySet<string> RevokedKeyIds { get; init; }
}

/// <summary>
/// Describes the roles that a principal holds according to the replicated authority documents.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record ReplicatedAuthority
{
    /// <summary>
    /// Gets the names of the roles held at the platform node, distinct and ordered ordinally.
    /// </summary>
    public IReadOnlyList<string> PlatformRoles { get; init; } = [];

    /// <summary>
    /// Gets the names of the roles held at the requested team and at every team above it, distinct and ordered
    /// ordinally. It is empty when no team was requested.
    /// </summary>
    public IReadOnlyList<string> TeamRoles { get; init; } = [];

    /// <summary>
    /// Gets the identifier of the control-plane client that the principal acts as, or <c>null</c> when the principal is
    /// not an enabled client of the control-plane tenant.
    /// </summary>
    public string? ClientId { get; init; }
}
