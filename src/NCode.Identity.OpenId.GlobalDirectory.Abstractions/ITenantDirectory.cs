// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Reserves tenant identifiers and issuers platform-wide, places each tenant in a cell, and remembers the identifiers
/// and issuers of deleted tenants. An implementation that cannot reach the directory throws
/// <see cref="DirectoryUnavailableException"/>. Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface ITenantDirectory
{
    /// <summary>
    /// Reserves the tenant identifier and, when the tenant has one, its issuer for a tenant about to be created; a
    /// tenant without an issuer is reserved by its identifier alone. The reservation is refused when another tenant
    /// holds the identifier or the issuer, when either is retired, or when the team already holds the most tenants its
    /// <c>max_tenants</c> setting allows. Reserving again for the cell, issuer and team of a reservation that is held
    /// and not yet placed succeeds, so a reservation that runs again after it took effect is not refused.
    /// </summary>
    /// <param name="reservation">The <see cref="TenantReservation"/> describing the identifiers to reserve.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ReservationResult"/> that is reserved, or a conflict with its detail.</returns>
    ValueTask<ReservationResult> ReserveAsync(
        TenantReservation reservation,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Places a tenant that the placement's cell reserved under the placement's issuer, or under none when the
    /// placement names none, once the cell holds the tenant's row. The placement is refused when nothing reserved the
    /// tenant, when the tenant is retired, or when the reservation belongs to another cell or issuer; placing a tenant
    /// already placed in that cell under that issuer succeeds. The directory takes the placement's region from the
    /// cell's registration.
    /// </summary>
    /// <param name="placement">The <see cref="TenantPlacementRequest"/> to record.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ReservationResult"/> that is reserved, or a conflict with its detail.</returns>
    ValueTask<ReservationResult> PlaceAsync(
        TenantPlacementRequest placement,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Releases the reservation of a tenant whose creation failed, so its identifier and issuer can be reserved again.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask ReleaseAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Retires the identifier and issuer of a deleted tenant. A retired identifier is never reissued, and only the
    /// tenant that held a retired issuer can take it back. The directory retires the issuer its record of the tenant
    /// holds, or the issuer the retirement names when it has no record, and retiring what is already retired succeeds.
    /// </summary>
    /// <param name="retirement">
    /// The <see cref="TenantRetirement"/> describing the identifier and issuer to retire.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RetireAsync(TenantRetirement retirement, CancellationToken cancellationToken);

    /// <summary>
    /// Checks a change of a tenant's issuer and retires the issuer the tenant gives up. The change is refused when
    /// another tenant holds the new issuer or retired it; a tenant can take back an issuer it retired itself.
    /// </summary>
    /// <param name="change">The <see cref="TenantIssuerChange"/> describing the previous and the new issuer.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ReservationResult"/> that is reserved, or a conflict with its detail.</returns>
    ValueTask<ReservationResult> ChangeIssuerAsync(
        TenantIssuerChange change,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Records that the cell holding a tenant offers the tenant to another cell of the same region, until the offer
    /// expires. A tenant has at most one offer, and offering again sets it to the new target cell and expiry. The offer
    /// is refused when the tenant is not placed in the source cell, when the cells are the same, when either cell is
    /// not registered, or when the cells are in different regions.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="fromCell">The name of the cell that holds the tenant and offers it.</param>
    /// <param name="toCell">The name of the cell that the tenant is offered to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="MoveOfferResult"/> that is offered, or a conflict with its detail.</returns>
    ValueTask<MoveOfferResult> OfferMoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Moves a placed tenant from one cell to another cell of the same region. The move consumes the source cell's
    /// unexpired offer of the tenant to the target cell, and is refused without one. The move is also refused when the
    /// tenant is not placed in the source cell, when either cell is not registered, or when the cells are in different
    /// regions. Moving a tenant that is already placed in the target cell succeeds, so a move that runs again after it
    /// took effect is not refused.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="fromCell">The name of the cell that holds the tenant.</param>
    /// <param name="toCell">The name of the cell that receives the tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="MoveResult"/> that is moved, or a conflict with its detail.</returns>
    ValueTask<MoveResult> MoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the placement of a tenant.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The <see cref="TenantPlacement"/> of the tenant, or <c>null</c> when the tenant is not placed.
    /// </returns>
    ValueTask<TenantPlacement?> GetPlacementOrDefaultAsync(
        string tenantId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Determines whether the specified tenant identifier belonged to a deleted tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to look up.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns><c>true</c> when a deleted tenant had the identifier; otherwise, <c>false</c>.</returns>
    ValueTask<bool> IsTenantIdRetiredAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the identifier of the tenant that gave up the specified issuer, by changing its issuer or by being deleted.
    /// </summary>
    /// <param name="issuer">The issuer to look up.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The identifier of the tenant that held the issuer, or <c>null</c> when no tenant has given it up.
    /// </returns>
    ValueTask<string?> GetRetiredIssuerHolderOrDefaultAsync(
        string issuer,
        CancellationToken cancellationToken
    );
}
