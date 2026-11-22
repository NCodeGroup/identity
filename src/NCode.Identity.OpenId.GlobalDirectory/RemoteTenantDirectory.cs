// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ITenantDirectory"/> of a cell that reaches the global directory service over HTTP. Each
/// method is one call under <c>directory/v1/</c>: <c>POST tenants/reserve</c>, <c>POST tenants/place</c>,
/// <c>POST tenants/{tenantId}/release</c>, <c>POST tenants/{tenantId}/move-offers</c>,
/// <c>POST tenants/{tenantId}/move?fromCell=&amp;toCell=</c>, <c>POST tenants/retire</c>,
/// <c>POST tenants/change-issuer</c>, <c>GET tenants/{tenantId}</c>, <c>GET tenants/retired-ids/{tenantId}</c> and
/// <c>GET retired-issuers?issuer=</c>. A <c>409</c> answer to a reservation, a move offer, a move or an issuer change
/// is a conflict result, and a <c>404</c> answer to a lookup means the tenant is not placed or the identifier is not
/// retired. Every failure of the directory is a <see cref="DirectoryUnavailableException"/>, so a cell never creates a
/// tenant while the directory is out of reach.
/// </summary>
// See ADR-1411.
internal sealed class RemoteTenantDirectory(DirectoryHttpClient client) : ITenantDirectory
{
    private const string Prefix = "tenants/";

    private DirectoryHttpClient Client { get; } = client;

    /// <inheritdoc />
    public async ValueTask<ReservationResult> ReserveAsync(
        TenantReservation reservation,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(reservation);

        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}reserve",
            reservation,
            cancellationToken
        );

        return ToReservationResult(response, "reserve a tenant");
    }

    /// <inheritdoc />
    public async ValueTask<ReservationResult> PlaceAsync(
        TenantPlacementRequest placement,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(placement);

        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}place",
            placement,
            cancellationToken
        );

        return ToReservationResult(response, "place a tenant");
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        var segment = DirectoryHttpClient.Segment(tenantId);
        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}{segment}/release",
            body: null,
            cancellationToken
        );

        response.EnsureSuccess("release a tenant");
    }

    /// <inheritdoc />
    public async ValueTask<MoveOfferResult> OfferMoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(fromCell);
        ArgumentException.ThrowIfNullOrEmpty(toCell);

        // The directory takes the source cell from the tenant's placement and the caller, so the body names only the
        // target.
        var segment = DirectoryHttpClient.Segment(tenantId);
        var offerRequest = new TenantMoveOfferRequest { ToCell = toCell };
        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}{segment}/move-offers",
            offerRequest,
            cancellationToken
        );

        if (response.IsConflict)
        {
            var detail = response.ReadProblemDetail(
                "The directory refused to record a move offer."
            );
            return MoveOfferResult.Conflict(detail);
        }

        response.EnsureSuccess("offer a tenant to another cell");
        return MoveOfferResult.Offered();
    }

    /// <inheritdoc />
    public async ValueTask<MoveResult> MoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(fromCell);
        ArgumentException.ThrowIfNullOrEmpty(toCell);

        var tenantSegment = DirectoryHttpClient.Segment(tenantId);
        var fromCellSegment = DirectoryHttpClient.Segment(fromCell);
        var toCellSegment = DirectoryHttpClient.Segment(toCell);
        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}{tenantSegment}/move?fromCell={fromCellSegment}&toCell={toCellSegment}",
            body: null,
            cancellationToken
        );

        if (response.IsConflict)
        {
            var detail = response.ReadProblemDetail("The directory refused to move a tenant.");
            return MoveResult.Conflict(detail);
        }

        response.EnsureSuccess("move a tenant");
        return MoveResult.Moved();
    }

    /// <inheritdoc />
    public async ValueTask RetireAsync(
        TenantRetirement retirement,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(retirement);

        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}retire",
            retirement,
            cancellationToken
        );

        response.EnsureSuccess("retire a tenant");
    }

    /// <inheritdoc />
    public async ValueTask<ReservationResult> ChangeIssuerAsync(
        TenantIssuerChange change,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(change);

        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}change-issuer",
            change,
            cancellationToken
        );

        if (response.IsNotFound)
        {
            var detail = response.ReadProblemDetail("The directory has no record of the tenant.");
            return ReservationResult.Conflict(detail);
        }

        return ToReservationResult(response, "change the issuer of a tenant");
    }

    /// <inheritdoc />
    public async ValueTask<TenantPlacement?> GetPlacementOrDefaultAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        var segment = DirectoryHttpClient.Segment(tenantId);
        var response = await Client.SendAsync(
            HttpMethod.Get,
            $"{Prefix}{segment}",
            body: null,
            cancellationToken
        );

        if (response.IsNotFound)
            return null;

        response.EnsureSuccess("get the placement of a tenant");
        return response.Read<TenantPlacement>();
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsTenantIdRetiredAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        var segment = DirectoryHttpClient.Segment(tenantId);
        var response = await Client.SendAsync(
            HttpMethod.Get,
            $"{Prefix}retired-ids/{segment}",
            body: null,
            cancellationToken
        );

        if (response.IsNotFound)
            return false;

        response.EnsureSuccess("look up a retired tenant identifier");
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetRetiredIssuerHolderOrDefaultAsync(
        string issuer,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(issuer);

        var segment = DirectoryHttpClient.Segment(issuer);
        var response = await Client.SendAsync(
            HttpMethod.Get,
            $"retired-issuers?issuer={segment}",
            body: null,
            cancellationToken
        );

        if (response.IsNotFound)
            return null;

        response.EnsureSuccess("look up a retired issuer");
        return response.Read<string>();
    }

    private static ReservationResult ToReservationResult(
        DirectoryResponse response,
        string operation
    )
    {
        if (response.IsConflict)
        {
            var detail = response.ReadProblemDetail($"The directory refused to {operation}.");
            return ReservationResult.Conflict(detail);
        }

        response.EnsureSuccess(operation);
        return ReservationResult.Reserved();
    }
}
