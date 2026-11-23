// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Reserves a tenant in the global directory and places it in this cell before the cell inserts the tenant's row, for
/// every creator of a tenant, so a tenant is reserved, placed and released alike however it is created. The reservation
/// names this cell and the tenant's team, or no team for a tenant of the platform's own team
/// (<see cref="TenantReservationRules.GetTeamId"/>). A placement that does not take effect, refused, failed or
/// canceled, releases the reservation on its own bounded token, and a release that fails is logged, so the refusal or
/// the failure that ended the placement surfaces; a refused reservation releases nothing. Register a custom
/// implementation to replace the default behavior.
/// </summary>
// See ADR-1411.
[PublicAPI]
public interface ITenantPlacer
{
    /// <summary>
    /// Reserves the tenant's identifier and, when it has one, its issuer, then places the tenant in this cell.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to create.</param>
    /// <param name="issuer">
    /// The tenant's issuer, or <see langword="null"/> when the tenant has none, which reserves and places its
    /// identifier alone.
    /// </param>
    /// <param name="teamId">The identifier of the team that holds the tenant in this cell, as the team holds
    /// it.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A <see cref="ReservationResult"/> that is reserved once the tenant is placed in this cell, or the directory's
    /// refusal of the reservation or of the placement, with its detail.
    /// </returns>
    /// <exception cref="DirectoryUnavailableException">The global directory cannot be reached.</exception>
    ValueTask<ReservationResult> ReserveAndPlaceAsync(
        string tenantId,
        string? issuer,
        string teamId,
        CancellationToken cancellationToken
    );
}
