// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

/// <summary>
/// Represents an entity framework data contract for the replica of a document that the global directory replicates into
/// the cell. The complimentary DTO for this entity is <see cref="PersistedReplicatedDocument"/>.
/// </summary>
/// <remarks>
/// This entity is intentionally not tenant-scoped: the replicated documents belong to the platform and its teams.
/// </remarks>
[PrimaryKey(nameof(Kind), nameof(Key))]
public sealed class ReplicatedDocumentEntity : ISupportConcurrencyToken
{
    /// <summary>
    /// Gets the kind of the document; with <see cref="Key"/> it is the primary key.
    /// </summary>
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceType)]
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the key of the document within its <see cref="Kind"/>.
    /// </summary>
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceId)]
    public required string Key { get; init; }

    /// <summary>
    /// Gets or sets the revision of the document.
    /// </summary>
    public long Revision { get; set; }

    /// <summary>
    /// Gets or sets the JSON content of the document, or <c>null</c> when the document is retired.
    /// </summary>
    public string? ContentJson { get; set; }

    /// <inheritdoc />
    [Unicode(false)]
    [MaxLength(MaxLengths.ConcurrencyToken)]
    [ConcurrencyCheck]
    public required string ConcurrencyToken { get; set; }
}
