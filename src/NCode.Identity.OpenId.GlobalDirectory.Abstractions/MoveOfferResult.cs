// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Represents the outcome of offering a tenant to another cell through
/// <see cref="ITenantDirectory.OfferMoveAsync"/>: the offer was recorded, or it conflicts with the tenant's placement
/// or with the cells.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class MoveOfferResult
{
    private MoveOfferResult(string? conflictDetail)
    {
        ConflictDetail = conflictDetail;
    }

    /// <summary>
    /// Gets the reason the offer was refused, or <c>null</c> when the offer was recorded.
    /// </summary>
    public string? ConflictDetail { get; }

    /// <summary>
    /// Gets a value indicating whether the offer was recorded, which is when <see cref="ConflictDetail"/> is
    /// <c>null</c>.
    /// </summary>
    [MemberNotNullWhen(false, nameof(ConflictDetail))]
    public bool IsOffered => ConflictDetail is null;

    /// <summary>
    /// Creates the result of an offer that was recorded.
    /// </summary>
    /// <returns>A <see cref="MoveOfferResult"/> whose <see cref="IsOffered"/> is <c>true</c>.</returns>
    public static MoveOfferResult Offered() => new(conflictDetail: null);

    /// <summary>
    /// Creates the result of an offer that was refused.
    /// </summary>
    /// <param name="detail">The reason the offer was refused.</param>
    /// <returns>A <see cref="MoveOfferResult"/> whose <see cref="IsOffered"/> is <c>false</c>.</returns>
    public static MoveOfferResult Conflict(string detail) => new(detail);
}
