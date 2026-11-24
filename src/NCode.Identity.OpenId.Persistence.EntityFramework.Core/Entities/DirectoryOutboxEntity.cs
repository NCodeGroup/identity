// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

/// <summary>
/// Represents an entity framework data contract for a document that the control-plane cell has committed and still has
/// to publish to the global directory. The complimentary DTO for this entity is
/// <see cref="PersistedDirectoryOutboxEntry"/>.
/// </summary>
/// <remarks>
/// This entity is intentionally not tenant-scoped: the outbox belongs to the platform. A row is written once and
/// removed once the directory acknowledges it.
/// </remarks>
public sealed class DirectoryOutboxEntity
{
    /// <summary>
    /// Gets the position of the entry in the outbox, which the database assigns and is the primary key.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Sequence { get; init; }

    /// <summary>
    /// Gets the kind of the document.
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
    /// Gets the revision of the document.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets the JSON content of the document, or <c>null</c> when the document is retired.
    /// </summary>
    public string? ContentJson { get; init; }

    /// <summary>
    /// Gets the instant at which the entry was written.
    /// </summary>
    public required DateTimeOffset CreatedWhen { get; init; }
}
