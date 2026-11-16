// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ITenantDirectory"/> of a cell that reaches the global directory service over HTTP. Each
/// method is one call under <c>directory/v1/tenants/</c>: <c>POST reserve</c>, <c>POST place</c>,
/// <c>POST {tenantId}/release</c>, <c>POST {tenantId}/move-offers</c>,
/// <c>POST {tenantId}/move?fromCell=&amp;toCell=</c>, <c>POST retire</c>, <c>POST change-issuer</c>,
/// <c>GET {tenantId}</c>, <c>GET retired-ids/{tenantId}</c> and <c>GET retired-issuers?issuer=</c>. A <c>409</c> answer
/// to a reservation, a move offer, a move or an issuer change is a conflict result, and a <c>404</c> answer to a lookup
/// means the tenant is not placed or the identifier is not retired. Every failure of the directory is a
/// <see cref="DirectoryUnavailableException"/>, so a cell never creates a tenant while the directory is out of reach.
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
        TenantPlacement placement,
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

        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}{DirectoryHttpClient.Segment(tenantId)}/release",
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
        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}{DirectoryHttpClient.Segment(tenantId)}/move-offers",
            new TenantMoveOfferRequest { ToCell = toCell },
            cancellationToken
        );

        if (response.IsConflict)
            return MoveOfferResult.Conflict(
                response.ReadProblemDetail("The directory refused to record a move offer.")
            );

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

        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}{DirectoryHttpClient.Segment(tenantId)}/move?fromCell={DirectoryHttpClient.Segment(fromCell)}&toCell={DirectoryHttpClient.Segment(toCell)}",
            body: null,
            cancellationToken
        );

        if (response.IsConflict)
            return MoveResult.Conflict(
                response.ReadProblemDetail("The directory refused to move a tenant.")
            );

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
            return ReservationResult.Conflict(
                response.ReadProblemDetail("The directory has no record of the tenant.")
            );

        return ToReservationResult(response, "change the issuer of a tenant");
    }

    /// <inheritdoc />
    public async ValueTask<TenantPlacement?> GetPlacementOrDefaultAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        var response = await Client.SendAsync(
            HttpMethod.Get,
            $"{Prefix}{DirectoryHttpClient.Segment(tenantId)}",
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

        var response = await Client.SendAsync(
            HttpMethod.Get,
            $"{Prefix}retired-ids/{DirectoryHttpClient.Segment(tenantId)}",
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

        var response = await Client.SendAsync(
            HttpMethod.Get,
            $"{Prefix}retired-issuers?issuer={DirectoryHttpClient.Segment(issuer)}",
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
            return ReservationResult.Conflict(
                response.ReadProblemDetail($"The directory refused to {operation}.")
            );

        response.EnsureSuccess(operation);
        return ReservationResult.Reserved();
    }
}
