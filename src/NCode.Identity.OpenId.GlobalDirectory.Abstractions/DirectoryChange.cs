// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes one entry of the directory's change feed. A change concerns exactly one team or the platform: a team
/// document published, a team retired, or the platform document published.
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
    /// Gets the identifier of the team the change concerns, or <c>null</c> for a platform change.
    /// </summary>
    public string? TeamId { get; init; }

    /// <summary>
    /// Gets the published team document, or <c>null</c> when the change retires the team or concerns the platform.
    /// </summary>
    public TeamDocument? Document { get; init; }

    /// <summary>
    /// Gets the published platform document. It is set only for a platform change.
    /// </summary>
    public PlatformDocument? Platform { get; init; }
}
