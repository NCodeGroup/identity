// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

/// <summary>
/// Represents an entity framework data contract for the counter that numbers the directory's changes. The table holds
/// exactly one row, which every transaction that writes a <see cref="DirectoryChangeRecord"/> increments, so the writers
/// serialize on the row and the commit order equals the sequence order.
/// </summary>
public sealed class DirectorySequenceRecord
{
    /// <summary>
    /// The surrogate identifier of the single row.
    /// </summary>
    public const long SingletonId = 1;

    /// <summary>
    /// Gets the surrogate identifier for this record, which is <see cref="SingletonId"/>.
    /// </summary>
    public long Id { get; init; } = SingletonId;

    /// <summary>
    /// Gets or sets the sequence of the last change that the directory wrote, or <c>0</c> when it wrote none.
    /// </summary>
    public long LastSequence { get; set; }
}
