// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

/// <summary>
/// Represents an entity framework data contract for the highest sequence of the directory's change feed that the cell
/// has applied.
/// </summary>
/// <remarks>
/// This entity is intentionally not tenant-scoped: a cell has one row, keyed by <see cref="SingletonKey"/>, that the
/// feed reads before a pass and raises after each page.
/// </remarks>
// See ADR-1416.
public sealed class DirectoryFeedCheckpointEntity : ISupportConcurrencyToken
{
    /// <summary>
    /// The value of <see cref="Key"/>, the only key this entity has.
    /// </summary>
    public const string SingletonKey = "directory-feed";

    /// <summary>
    /// Gets the natural key for this entity, always <see cref="SingletonKey"/>: the primary key on it keeps the table
    /// to one row.
    /// </summary>
    [Key]
    [Unicode(false)]
    [MaxLength(MaxLengths.ResourceId)]
    public required string Key { get; init; }

    /// <summary>
    /// Gets or sets the highest sequence of the directory's change feed that the cell has applied.
    /// </summary>
    public long AppliedSequence { get; set; }

    /// <inheritdoc />
    [Unicode(false)]
    [MaxLength(MaxLengths.ConcurrencyToken)]
    [ConcurrencyCheck]
    public required string ConcurrencyToken { get; set; }
}
