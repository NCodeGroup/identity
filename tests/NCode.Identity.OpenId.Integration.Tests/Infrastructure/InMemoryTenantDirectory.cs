// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.OpenId.Integration.Tests.Infrastructure;

/// <summary>
/// An <see cref="ITenantDirectory"/> over a dictionary, which hosts that stand for different cells of one region share,
/// so a tenant that one cell placed is placed in the directory every other cell asks. It follows the rules of the
/// global directory: identifiers and issuers compare without regard to case, a retired identifier is never reissued,
/// only the tenant that gave up an issuer takes it back, a reservation or a move that runs again after it took effect
/// succeeds, <see cref="OfferMoveAsync"/> records the offer of a placed tenant to another registered cell of its
/// region, and <see cref="MoveAsync"/> moves the tenant only on that unexpired offer.
/// </summary>
public sealed class InMemoryTenantDirectory : ITenantDirectory
{
    // The lifetime of a move offer, which equals the lifetime of the transfer key that the target cell issues.
    private const int MoveOfferLifetimeHours = 1;

    private Lock Gate { get; } = new();

    private Dictionary<string, string> CellRegions { get; }

    private Dictionary<string, Offer> Offers { get; } = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, Entry> Tenants { get; } = new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string> RetiredTenantIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, string> RetiredIssuers { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryTenantDirectory"/> class.
    /// </summary>
    /// <param name="cellRegions">The registered cells, each with the region that it runs in.</param>
    public InMemoryTenantDirectory(IReadOnlyDictionary<string, string> cellRegions) =>
        CellRegions = new Dictionary<string, string>(cellRegions, StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<ReservationResult> ReserveAsync(
        TenantReservation reservation,
        CancellationToken cancellationToken
    )
    {
        lock (Gate)
        {
            if (RetiredTenantIds.Contains(reservation.TenantId))
            {
                return ValueTask.FromResult(
                    ReservationResult.Conflict(
                        $"The tenant identifier '{reservation.TenantId}' belonged to a deleted tenant and is never reissued."
                    )
                );
            }

            if (Tenants.TryGetValue(reservation.TenantId, out var existing))
            {
                // A reservation that runs again after its commit's acknowledgement is lost finds its own record.
                if (
                    !existing.IsPlaced
                    && string.Equals(existing.Cell, reservation.Cell, StringComparison.Ordinal)
                    && string.Equals(
                        existing.Issuer,
                        reservation.Issuer,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return ValueTask.FromResult(ReservationResult.Reserved());
                }

                return ValueTask.FromResult(
                    ReservationResult.Conflict(
                        $"Another tenant already has the identifier '{reservation.TenantId}'."
                    )
                );
            }

            if (HoldsIssuer(reservation.Issuer))
            {
                return ValueTask.FromResult(
                    ReservationResult.Conflict(
                        $"Another tenant already has the issuer '{reservation.Issuer}'."
                    )
                );
            }

            if (
                RetiredIssuers.TryGetValue(reservation.Issuer, out var formerHolder)
                && !string.Equals(
                    formerHolder,
                    reservation.TenantId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return ValueTask.FromResult(
                    ReservationResult.Conflict(
                        $"The issuer '{reservation.Issuer}' belonged to another tenant and is never reissued."
                    )
                );
            }

            Tenants[reservation.TenantId] = new Entry
            {
                TenantId = reservation.TenantId,
                Issuer = reservation.Issuer,
                Cell = reservation.Cell,
            };
            return ValueTask.FromResult(ReservationResult.Reserved());
        }
    }

    /// <inheritdoc />
    public ValueTask<ReservationResult> PlaceAsync(
        TenantPlacement placement,
        CancellationToken cancellationToken
    )
    {
        lock (Gate)
        {
            if (RetiredTenantIds.Contains(placement.TenantId))
            {
                return ValueTask.FromResult(
                    ReservationResult.Conflict(
                        $"The tenant identifier '{placement.TenantId}' belonged to a deleted tenant and is never reissued."
                    )
                );
            }

            if (!Tenants.TryGetValue(placement.TenantId, out var entry))
            {
                return ValueTask.FromResult(
                    ReservationResult.Conflict(
                        $"The tenant '{placement.TenantId}' is not reserved."
                    )
                );
            }

            if (
                !string.Equals(entry.Cell, placement.Cell, StringComparison.Ordinal)
                || !string.Equals(
                    entry.Issuer,
                    placement.Issuer,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return ValueTask.FromResult(
                    ReservationResult.Conflict(
                        $"The tenant '{placement.TenantId}' is reserved for another cell or issuer."
                    )
                );
            }

            entry.IsPlaced = true;
            return ValueTask.FromResult(ReservationResult.Reserved());
        }
    }

    /// <inheritdoc />
    public ValueTask ReleaseAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (Gate)
        {
            if (Tenants.TryGetValue(tenantId, out var entry) && !entry.IsPlaced)
            {
                Tenants.Remove(tenantId);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public ValueTask RetireAsync(TenantRetirement retirement, CancellationToken cancellationToken)
    {
        lock (Gate)
        {
            Tenants.Remove(retirement.TenantId);
            RetiredTenantIds.Add(retirement.TenantId);

            if (retirement.Issuer is { } issuer)
            {
                RetiredIssuers[issuer] = retirement.TenantId;
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public ValueTask<ReservationResult> ChangeIssuerAsync(
        TenantIssuerChange change,
        CancellationToken cancellationToken
    )
    {
        lock (Gate)
        {
            if (change.Issuer is { } issuer)
            {
                if (HoldsIssuer(issuer, except: change.TenantId))
                {
                    return ValueTask.FromResult(
                        ReservationResult.Conflict(
                            $"Another tenant already has the issuer '{issuer}'."
                        )
                    );
                }

                if (
                    RetiredIssuers.TryGetValue(issuer, out var formerHolder)
                    && !string.Equals(
                        formerHolder,
                        change.TenantId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return ValueTask.FromResult(
                        ReservationResult.Conflict(
                            $"The issuer '{issuer}' belonged to another tenant and is never reissued."
                        )
                    );
                }
            }

            if (
                change.PreviousIssuer is { } previousIssuer
                && !string.Equals(previousIssuer, change.Issuer, StringComparison.OrdinalIgnoreCase)
            )
            {
                RetiredIssuers[previousIssuer] = change.TenantId;
            }

            if (Tenants.TryGetValue(change.TenantId, out var entry))
            {
                entry.Issuer = change.Issuer;
            }

            return ValueTask.FromResult(ReservationResult.Reserved());
        }
    }

    /// <inheritdoc />
    public ValueTask<MoveOfferResult> OfferMoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    )
    {
        lock (Gate)
        {
            if (!Tenants.TryGetValue(tenantId, out var entry) || !entry.IsPlaced)
            {
                return ValueTask.FromResult(
                    MoveOfferResult.Conflict($"The tenant '{tenantId}' is not placed in a cell.")
                );
            }

            if (!string.Equals(entry.Cell, fromCell, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    MoveOfferResult.Conflict(
                        $"The tenant '{tenantId}' is not placed in the cell '{fromCell}'."
                    )
                );
            }

            if (string.Equals(fromCell, toCell, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    MoveOfferResult.Conflict(
                        $"The tenant '{tenantId}' is already placed in the cell '{toCell}'."
                    )
                );
            }

            if (!CellRegions.TryGetValue(fromCell, out var fromRegion))
            {
                return ValueTask.FromResult(
                    MoveOfferResult.Conflict($"The cell '{fromCell}' is not registered.")
                );
            }

            if (!CellRegions.TryGetValue(toCell, out var toRegion))
            {
                return ValueTask.FromResult(
                    MoveOfferResult.Conflict($"The cell '{toCell}' is not registered.")
                );
            }

            if (!string.Equals(fromRegion, toRegion, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    MoveOfferResult.Conflict(
                        $"The cells '{fromCell}' and '{toCell}' are in different regions, and a tenant moves only within its region."
                    )
                );
            }

            Offers[tenantId] = new Offer
            {
                FromCell = fromCell,
                ToCell = toCell,
                ExpiresWhen = TimeProvider.System.GetUtcNow().AddHours(MoveOfferLifetimeHours),
            };
            return ValueTask.FromResult(MoveOfferResult.Offered());
        }
    }

    /// <inheritdoc />
    public ValueTask<MoveResult> MoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    )
    {
        lock (Gate)
        {
            if (!Tenants.TryGetValue(tenantId, out var entry) || !entry.IsPlaced)
            {
                return ValueTask.FromResult(
                    MoveResult.Conflict($"The tenant '{tenantId}' is not placed in a cell.")
                );
            }

            if (!string.Equals(entry.Cell, fromCell, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    MoveResult.Conflict(
                        $"The tenant '{tenantId}' is not placed in the cell '{fromCell}'."
                    )
                );
            }

            if (string.Equals(fromCell, toCell, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    MoveResult.Conflict(
                        $"The tenant '{tenantId}' is already placed in the cell '{toCell}'."
                    )
                );
            }

            if (!CellRegions.TryGetValue(fromCell, out var fromRegion))
            {
                return ValueTask.FromResult(
                    MoveResult.Conflict($"The cell '{fromCell}' is not registered.")
                );
            }

            if (!CellRegions.TryGetValue(toCell, out var toRegion))
            {
                return ValueTask.FromResult(
                    MoveResult.Conflict($"The cell '{toCell}' is not registered.")
                );
            }

            if (!string.Equals(fromRegion, toRegion, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(
                    MoveResult.Conflict(
                        $"The cells '{fromCell}' and '{toCell}' are in different regions, and a tenant moves only within its region."
                    )
                );
            }

            if (
                !Offers.TryGetValue(tenantId, out var offer)
                || !string.Equals(offer.FromCell, fromCell, StringComparison.Ordinal)
                || !string.Equals(offer.ToCell, toCell, StringComparison.Ordinal)
            )
            {
                return ValueTask.FromResult(
                    MoveResult.Conflict(
                        $"The cell '{fromCell}' has not offered the tenant '{tenantId}' to the cell '{toCell}'."
                    )
                );
            }

            if (offer.ExpiresWhen <= TimeProvider.System.GetUtcNow())
            {
                return ValueTask.FromResult(
                    MoveResult.Conflict(
                        $"The offer of the tenant '{tenantId}' to the cell '{toCell}' has expired."
                    )
                );
            }

            entry.Cell = toCell;
            Offers.Remove(tenantId);
            return ValueTask.FromResult(MoveResult.Moved());
        }
    }

    /// <inheritdoc />
    public ValueTask<TenantPlacement?> GetPlacementOrDefaultAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        lock (Gate)
        {
            if (
                !Tenants.TryGetValue(tenantId, out var entry)
                || !entry.IsPlaced
                || entry.Cell is not { } cell
            )
            {
                return ValueTask.FromResult<TenantPlacement?>(null);
            }

            return ValueTask.FromResult<TenantPlacement?>(
                new TenantPlacement
                {
                    TenantId = entry.TenantId,
                    Issuer = entry.Issuer ?? string.Empty,
                    Cell = cell,
                    Region = CellRegions.GetValueOrDefault(cell) ?? string.Empty,
                }
            );
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> IsTenantIdRetiredAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        lock (Gate)
        {
            return ValueTask.FromResult(RetiredTenantIds.Contains(tenantId));
        }
    }

    /// <inheritdoc />
    public ValueTask<string?> GetRetiredIssuerHolderOrDefaultAsync(
        string issuer,
        CancellationToken cancellationToken
    )
    {
        lock (Gate)
        {
            return ValueTask.FromResult(RetiredIssuers.GetValueOrDefault(issuer));
        }
    }

    private bool HoldsIssuer(string issuer, string? except = null) =>
        Tenants.Values.Any(entry =>
            string.Equals(entry.Issuer, issuer, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.TenantId, except, StringComparison.OrdinalIgnoreCase)
        );

    private sealed class Entry
    {
        public required string TenantId { get; init; }

        public string? Issuer { get; set; }

        public string? Cell { get; set; }

        public bool IsPlaced { get; set; }
    }

    private sealed class Offer
    {
        public required string FromCell { get; init; }

        public required string ToCell { get; init; }

        public required DateTimeOffset ExpiresWhen { get; init; }
    }
}
