// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Represents the outcome of moving a tenant's placement between cells through
/// <see cref="ITenantDirectory.MoveAsync"/>: the placement moved, or the move conflicts with the tenant's placement or
/// with the cells.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class MoveResult
{
    private MoveResult(string? conflictDetail)
    {
        ConflictDetail = conflictDetail;
    }

    /// <summary>
    /// Gets the reason the move was refused, or <c>null</c> when the placement moved.
    /// </summary>
    public string? ConflictDetail { get; }

    /// <summary>
    /// Gets a value indicating whether the placement moved, which is when <see cref="ConflictDetail"/> is <c>null</c>.
    /// </summary>
    [MemberNotNullWhen(false, nameof(ConflictDetail))]
    public bool IsMoved => ConflictDetail is null;

    /// <summary>
    /// Creates the result of a move that succeeded.
    /// </summary>
    /// <returns>A <see cref="MoveResult"/> whose <see cref="IsMoved"/> is <c>true</c>.</returns>
    public static MoveResult Moved() => new(conflictDetail: null);

    /// <summary>
    /// Creates the result of a move that was refused.
    /// </summary>
    /// <param name="detail">The reason the move was refused.</param>
    /// <returns>A <see cref="MoveResult"/> whose <see cref="IsMoved"/> is <c>false</c>.</returns>
    public static MoveResult Conflict(string detail) => new(detail);
}
