// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

/// <summary>
/// Represents an entity framework data contract for the latest revision of one replicated document that the
/// control-plane cell published: the platform's authority, a team's authority or the management keys. The table holds
/// one row per kind and key.
/// </summary>
public sealed class ReplicatedDocumentRecord
{
    /// <summary>
    /// Gets the kind of the document, one of the <c>ReplicatedDocumentKinds</c> values.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the key of the document within its kind: the normalized team identifier of a team authority document, and
    /// empty for every other kind.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets or sets the revision of the stored document.
    /// </summary>
    public long Revision { get; set; }

    /// <summary>
    /// Gets or sets the content of the document as the publishing cell's schema-versioned <c>$ver</c> and
    /// <c>$payload</c> envelope, or <c>null</c> for a retired document.
    /// </summary>
    /// <remarks>
    /// The text is relayed as the cell published it, never reshaped, so it is not plain JSON content.
    /// </remarks>
    // See ADR-1536.
    public string? DocumentJson { get; set; }
}
