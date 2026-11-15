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
    /// Gets or sets the JSON of the published document, or <c>null</c> for a team retire.
    /// </summary>
    public string? DocumentJson { get; set; }
}
