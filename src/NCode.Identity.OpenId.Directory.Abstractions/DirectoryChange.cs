// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Directory;

/// <summary>
/// Describes one entry of the directory's team change feed: a team document published, or a team retired.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record DirectoryChange
{
    /// <summary>
    /// Gets the position of the change in the feed. A later change has a greater sequence.
    /// </summary>
    public required long Sequence { get; init; }

    /// <summary>
    /// Gets the identifier of the team the change concerns.
    /// </summary>
    public required string TeamId { get; init; }

    /// <summary>
    /// Gets the published team document, or <c>null</c> when the change retires the team.
    /// </summary>
    public TeamDocument? Document { get; init; }
}
