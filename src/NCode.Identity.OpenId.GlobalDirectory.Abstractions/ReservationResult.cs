// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Represents the outcome of reserving an identifier through <see cref="ITenantDirectory"/> or
/// <see cref="ITeamDirectory"/>: the identifier is reserved, or it conflicts with a holder, a retired identifier or a
/// limit.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class ReservationResult
{
    private ReservationResult(string? conflictDetail)
    {
        ConflictDetail = conflictDetail;
    }

    /// <summary>
    /// Gets the reason the reservation was refused, or <c>null</c> when the identifier is reserved.
    /// </summary>
    public string? ConflictDetail { get; }

    /// <summary>
    /// Gets a value indicating whether the identifier is reserved, which is when <see cref="ConflictDetail"/> is
    /// <c>null</c>.
    /// </summary>
    [MemberNotNullWhen(false, nameof(ConflictDetail))]
    public bool IsReserved => ConflictDetail is null;

    /// <summary>
    /// Creates the result of a reservation that succeeded.
    /// </summary>
    /// <returns>A <see cref="ReservationResult"/> whose <see cref="IsReserved"/> is <c>true</c>.</returns>
    public static ReservationResult Reserved() => new(conflictDetail: null);

    /// <summary>
    /// Creates the result of a reservation that was refused.
    /// </summary>
    /// <param name="detail">The reason the reservation was refused.</param>
    /// <returns>A <see cref="ReservationResult"/> whose <see cref="IsReserved"/> is <c>false</c>.</returns>
    public static ReservationResult Conflict(string detail) => new(detail);
}
