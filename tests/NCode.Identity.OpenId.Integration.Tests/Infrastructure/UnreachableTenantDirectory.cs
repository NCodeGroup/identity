// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.OpenId.Integration.Tests.Infrastructure;

/// <summary>
/// An <see cref="ITenantDirectory"/> whose every call fails as an unreachable directory does, as the control-plane
/// cell's remote directory does before the cell serves the token the directory requires.
/// </summary>
internal sealed class UnreachableTenantDirectory : ITenantDirectory
{
    /// <inheritdoc />
    public ValueTask<ReservationResult> ReserveAsync(
        TenantReservation reservation,
        CancellationToken cancellationToken
    ) => throw Unavailable();

    /// <inheritdoc />
    public ValueTask<ReservationResult> PlaceAsync(
        TenantPlacementRequest placement,
        CancellationToken cancellationToken
    ) => throw Unavailable();

    /// <inheritdoc />
    public ValueTask ReleaseAsync(string tenantId, CancellationToken cancellationToken) =>
        throw Unavailable();

    /// <inheritdoc />
    public ValueTask RetireAsync(
        TenantRetirement retirement,
        CancellationToken cancellationToken
    ) => throw Unavailable();

    /// <inheritdoc />
    public ValueTask<ReservationResult> ChangeIssuerAsync(
        TenantIssuerChange change,
        CancellationToken cancellationToken
    ) => throw Unavailable();

    /// <inheritdoc />
    public ValueTask<MoveOfferResult> OfferMoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    ) => throw Unavailable();

    /// <inheritdoc />
    public ValueTask<MoveResult> MoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    ) => throw Unavailable();

    /// <inheritdoc />
    public ValueTask<TenantPlacement?> GetPlacementOrDefaultAsync(
        string tenantId,
        CancellationToken cancellationToken
    ) => throw Unavailable();

    /// <inheritdoc />
    public ValueTask<bool> IsTenantIdRetiredAsync(
        string tenantId,
        CancellationToken cancellationToken
    ) => throw Unavailable();

    /// <inheritdoc />
    public ValueTask<string?> GetRetiredIssuerHolderOrDefaultAsync(
        string issuer,
        CancellationToken cancellationToken
    ) => throw Unavailable();

    private static DirectoryUnavailableException Unavailable() =>
        new("The directory cannot be reached.");
}
