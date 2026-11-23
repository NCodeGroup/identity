// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Tenants;

/// <summary>
/// Provides the default implementation of <see cref="ITenantPlacer"/> over the registered
/// <see cref="ITenantDirectory"/>, which is the global directory's remote adapter or this cell's local one.
/// </summary>
// See ADR-1351 and ADR-1411.
internal sealed class DefaultTenantPlacer(
    ITenantDirectory tenantDirectory,
    IOptions<CellOptions> cellOptionsAccessor,
    IOptions<TenantResolutionOptions> optionsAccessor,
    TimeProvider timeProvider,
    ILogger<DefaultTenantPlacer> logger
) : ITenantPlacer
{
    /// <summary>
    /// Gets the global directory that reserves and places the tenant.
    /// </summary>
    private ITenantDirectory TenantDirectory { get; } = tenantDirectory;

    /// <summary>
    /// Gets the options of the cell that the host runs.
    /// </summary>
    private CellOptions CellOptions { get; } = cellOptionsAccessor.Value;

    /// <summary>
    /// Gets the time provider that bounds the release of a reservation.
    /// </summary>
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <summary>
    /// Gets the logger that records a release that failed.
    /// </summary>
    private ILogger<DefaultTenantPlacer> Logger { get; } = logger;

    /// <summary>
    /// Gets the options of the root tenant, which name the platform's own team.
    /// </summary>
    private RootTenantOptions RootOptions => optionsAccessor.Value.Root;

    /// <inheritdoc />
    public async ValueTask<ReservationResult> ReserveAndPlaceAsync(
        string tenantId,
        string? issuer,
        string teamId,
        CancellationToken cancellationToken
    )
    {
        // A tenant of the platform's own team is reserved in no team, because the directory holds no record of that
        // team (ADR-1411).
        var reservedTeamId = TenantReservationRules.GetTeamId(teamId, RootOptions.TeamId);
        var reservation = new TenantReservation
        {
            TenantId = tenantId,
            Issuer = issuer,
            TeamId = reservedTeamId,
            Cell = CellOptions.Name,
        };
        var reservationResult = await TenantDirectory.ReserveAsync(reservation, cancellationToken);
        if (!reservationResult.IsReserved)
        {
            // A refused reservation holds nothing of this creation's, so nothing is released.
            return reservationResult;
        }

        var isPlaced = false;
        try
        {
            var placementRequest = new TenantPlacementRequest
            {
                TenantId = tenantId,
                Issuer = issuer,
                Cell = CellOptions.Name,
            };
            var placementResult = await TenantDirectory.PlaceAsync(
                placementRequest,
                cancellationToken
            );
            isPlaced = placementResult.IsReserved;
            return placementResult;
        }
        finally
        {
            if (!isPlaced)
            {
                // A placement that did not take effect releases the reservation, even when the creation was canceled,
                // on its own token bounded as work that follows a commit, and a release that fails or outlasts its
                // bound is logged, so the failure that ended the creation surfaces. A release frees only a tenant that
                // is not placed, so it never frees the placement of a concurrent creator of the tenant in this cell,
                // and a placement whose acknowledgement was lost stays for the next creation of the tenant in this
                // cell to complete. See ADR-1351 and ADR-1411.
                try
                {
                    await PostCommitWork.RunAsync(
                        TimeProvider,
                        TenantDirectory.ReleaseAsync,
                        tenantId
                    );
                }
                catch (Exception exception)
                {
                    Logger.TenantCreationReleaseFailed(tenantId, exception);
                }
            }
        }
    }
}
