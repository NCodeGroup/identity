// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

/// <summary>
/// Represents an entity framework data contract for one change that the cells mirror: a team publish, a team retire or
/// a platform publish. The <see cref="Sequence"/> orders the changes.
/// </summary>
public sealed class DirectoryChangeRecord
{
    /// <summary>
    /// Gets the sequence of the change, which the directory assigns from the <see cref="DirectorySequenceRecord"/>
    /// counter.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// Gets or sets the identifier of the team that changed, or <c>null</c> for a platform change.
    /// </summary>
    public string? TeamId { get; set; }

    /// <summary>
    /// Gets or sets the published document as its schema-versioned JSON envelope, or <c>null</c> for a team retire.
    /// </summary>
    /// <remarks>
    /// The column keeps the envelope as text, as the team and platform rows do: the store writes one text to the change
    /// and to the row whose document it carries, and the change feed parses it only when it reads the change.
    /// </remarks>
    // See ADR-1411.
    public string? DocumentJson { get; set; }
}
