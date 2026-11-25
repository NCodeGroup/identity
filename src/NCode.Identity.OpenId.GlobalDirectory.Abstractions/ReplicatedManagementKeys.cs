// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Secrets.Keys;

namespace NCode.Identity.OpenId.GlobalDirectory;

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
