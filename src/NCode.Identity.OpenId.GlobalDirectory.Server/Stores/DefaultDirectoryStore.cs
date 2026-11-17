// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Stores;

/// <summary>
/// Provides the <see cref="DirectoryDbContext"/> store behind every route of the directory: the tenant, team, cell and
/// platform directories. Every call is its own atomic operation: it opens a <see cref="DirectoryDbContext"/> of its own
/// and commits its writes before it returns. A call that decides from what it reads before it writes, such as a
/// reservation, runs in a serializable transaction.
/// </summary>
/// <remarks>
/// A tenant's limit is enforced from the documents alone. The platform document and the team documents carry the
/// <c>max_tenants</c> setting, and each team document carries its parent team's identifier, so the directory knows the
/// lineage and the subtree of every team without any other source. A cell's root tenant is never reserved.
/// </remarks>
// See ADR-1160.
// See ADR-1411.
internal sealed class DefaultDirectoryStore(
    IDbContextFactory<DirectoryDbContext> dbContextFactory,
    IOptions<DirectoryServerOptions> options,
    IPersistedPayloadSerializer payloadSerializer,
    TimeProvider timeProvider
)
    : ITenantDirectory,
        IDirectoryTenantWriter,
        IDirectoryTeamWriter,
        ITeamDirectory,
        ICellDirectory,
        IPlatformDirectory
{
    private const string WriteForbiddenDetail =
        "Only the cell that holds a tenant, or the control-plane cell, writes it.";

    // The lifetime of a move offer in hours. It equals the lifetime of the one-time transfer key that the target cell
    // issues for the move (DefaultTenantTransferKeyService.Lifetime), so an offer does not outlive the key that its
    // export is sealed under.
    private const int MoveOfferLifetimeHours = 1;

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private IDbContextFactory<DirectoryDbContext> DbContextFactory { get; } = dbContextFactory;

    private IOptions<DirectoryServerOptions> ServerOptions { get; } = options;

    /// <summary>
    /// Gets the serializer that writes and reads the schema-versioned envelope of every stored team and platform
    /// document.
    /// </summary>
    private IPersistedPayloadSerializer PayloadSerializer { get; } = payloadSerializer;

    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <summary>
    /// Normalizes an identifier or an issuer into the lower-case form that the directory stores and compares, so a
    /// lookup ignores case.
    /// </summary>
    /// <param name="value">The identifier or issuer to normalize.</param>
    /// <returns>The invariant lower-case form of <paramref name="value"/>.</returns>
    private static string Normalize(string value) => value.ToLowerInvariant();

    /// <summary>
    /// Increments the counter row that numbers the changes and returns the new value. The caller runs in a serializable
    /// transaction, so the writers of changes serialize on the row and the commit order equals the sequence order.
    /// </summary>
    /// <param name="dbContext">The <see cref="DirectoryDbContext"/> whose transaction holds the counter row.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The new value of the counter, which is the sequence number of the change.</returns>
    private static async ValueTask<long> NextSequenceAsync(
        DirectoryDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var counter = await dbContext.Sequences.SingleOrDefaultAsync(
            entity => entity.Id == DirectorySequenceRecord.SingletonId,
            cancellationToken
        );
        if (counter is null)
        {
            // Two first writers both insert the row; the serializable transaction fails one, and its execution
            // strategy runs it again over the row the other committed.
            counter = new DirectorySequenceRecord();
            dbContext.Sequences.Add(counter);
        }

        counter.LastSequence++;
        return counter.LastSequence;
    }

    #region ITenantDirectory

    /// <inheritdoc />
    async ValueTask<ReservationResult> ITenantDirectory.ReserveAsync(
        TenantReservation reservation,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async _ =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken
                    );

                    var tenantId = reservation.TenantId;
                    var normalizedTenantId = Normalize(tenantId);
                    var normalizedIssuer = Normalize(reservation.Issuer);
                    var normalizedTeamId = reservation.TeamId is { } requestedTeamId
                        ? Normalize(requestedTeamId)
                        : null;

                    var existing = await dbContext
                        .Tenants.AsNoTracking()
                        .Where(entity => entity.NormalizedTenantId == normalizedTenantId)
                        .Select(entity => new
                        {
                            entity.State,
                            entity.Cell,
                            entity.NormalizedIssuer,
                            entity.NormalizedTeamId,
                        })
                        .SingleOrDefaultAsync(cancellationToken);
                    if (existing?.State == TenantRecord.StateRetired)
                    {
                        return ReservationResult.Conflict(
                            $"The tenant identifier '{tenantId}' belonged to a deleted tenant and is never reissued."
                        );
                    }

                    if (existing is not null)
                    {
                        // A reservation that runs again after its commit's acknowledgement is lost finds its own
                        // record: the calling cell holds it for the same issuer and team, and no placement has
                        // consumed it.
                        if (
                            existing.State == TenantRecord.StateReserved
                            && string.Equals(
                                existing.Cell,
                                reservation.Cell,
                                StringComparison.Ordinal
                            )
                            && string.Equals(
                                existing.NormalizedIssuer,
                                normalizedIssuer,
                                StringComparison.Ordinal
                            )
                            && string.Equals(
                                existing.NormalizedTeamId,
                                normalizedTeamId,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            return ReservationResult.Reserved();
                        }

                        return ReservationResult.Conflict(
                            $"Another tenant already has the identifier '{tenantId}'."
                        );
                    }

                    if (
                        await dbContext.Tenants.AnyAsync(
                            entity => entity.NormalizedIssuer == normalizedIssuer,
                            cancellationToken
                        )
                    )
                    {
                        return ReservationResult.Conflict(
                            $"Another tenant already has the issuer '{reservation.Issuer}'."
                        );
                    }

                    var retiredIssuerDetail = await FindRetiredIssuerConflictAsync(
                        dbContext,
                        tenantId,
                        reservation.Issuer,
                        cancellationToken
                    );
                    if (retiredIssuerDetail is not null)
                    {
                        return ReservationResult.Conflict(retiredIssuerDetail);
                    }

                    if (reservation.TeamId is { } teamId)
                    {
                        var fullTeamDetail = await FindTeamAtLimitAsync(
                            dbContext,
                            teamId,
                            cancellationToken
                        );
                        if (fullTeamDetail is not null)
                        {
                            return ReservationResult.Conflict(fullTeamDetail);
                        }
                    }

                    var now = TimeProvider.GetUtcNow();
                    dbContext.Tenants.Add(
                        new TenantRecord
                        {
                            TenantId = tenantId,
                            NormalizedTenantId = normalizedTenantId,
                            Issuer = reservation.Issuer,
                            NormalizedIssuer = normalizedIssuer,
                            NormalizedTeamId = normalizedTeamId,
                            Cell = reservation.Cell,
                            State = TenantRecord.StateReserved,
                            CreatedWhen = now,
                            ModifiedWhen = now,
                        }
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return ReservationResult.Reserved();
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    async ValueTask<ReservationResult> ITenantDirectory.PlaceAsync(
        TenantPlacement placement,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async _ =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken
                    );

                    var tenantId = placement.TenantId;
                    var normalizedTenantId = Normalize(tenantId);
                    var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                        entity => entity.NormalizedTenantId == normalizedTenantId,
                        cancellationToken
                    );
                    if (tenant is null)
                    {
                        return ReservationResult.Conflict(
                            $"The tenant '{tenantId}' is not reserved."
                        );
                    }

                    if (tenant.State == TenantRecord.StateRetired)
                    {
                        // A retired identifier is never reissued, so a placement cannot bring it back.
                        return ReservationResult.Conflict(
                            $"The tenant identifier '{tenantId}' belonged to a deleted tenant and is never reissued."
                        );
                    }

                    if (
                        !string.Equals(tenant.Cell, placement.Cell, StringComparison.Ordinal)
                        || !string.Equals(
                            tenant.NormalizedIssuer,
                            Normalize(placement.Issuer),
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return ReservationResult.Conflict(
                            $"The tenant '{tenantId}' is reserved for another cell or issuer."
                        );
                    }

                    if (tenant.State == TenantRecord.StatePlaced)
                    {
                        return ReservationResult.Reserved();
                    }

                    tenant.State = TenantRecord.StatePlaced;
                    tenant.ModifiedWhen = TimeProvider.GetUtcNow();

                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return ReservationResult.Reserved();
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    async ValueTask ITenantDirectory.ReleaseAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        // A write through ITenantDirectory is the directory's own and carries every cell's authority.
        await ReleaseCoreAsync(tenantId, callerCell: null, cancellationToken);
    }

    /// <summary>
    /// Releases the reservation of a tenant in a serializable transaction. A tenant with no record has nothing to
    /// release, and a tenant that is not in the reserved state keeps its record.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant whose reservation is released.</param>
    /// <param name="callerCell">The name of the calling cell, or <c>null</c> for the directory's own call.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A forbidden result when the caller's cell does not hold the tenant and is not the control-plane cell; otherwise,
    /// a done result.
    /// </returns>
    private async ValueTask<DirectoryTenantWriteResult> ReleaseCoreAsync(
        string tenantId,
        string? callerCell,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async _ =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken
                    );

                    var normalizedTenantId = Normalize(tenantId);
                    var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                        entity => entity.NormalizedTenantId == normalizedTenantId,
                        cancellationToken
                    );
                    if (tenant is null)
                    {
                        // A tenant with no record has nothing to release, whoever asks.
                        return DirectoryTenantWriteResult.Done();
                    }

                    if (!IsAuthorized(callerCell, tenant.Cell))
                    {
                        return DirectoryTenantWriteResult.Forbidden(WriteForbiddenDetail);
                    }

                    if (tenant.State != TenantRecord.StateReserved)
                    {
                        return DirectoryTenantWriteResult.Done();
                    }

                    dbContext.Tenants.Remove(tenant);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return DirectoryTenantWriteResult.Done();
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    async ValueTask<MoveResult> ITenantDirectory.MoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async _ =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken
                    );

                    var normalizedTenantId = Normalize(tenantId);
                    var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                        entity =>
                            entity.NormalizedTenantId == normalizedTenantId
                            && entity.State == TenantRecord.StatePlaced,
                        cancellationToken
                    );
                    if (tenant is null)
                    {
                        return MoveResult.Conflict(
                            $"The tenant '{tenantId}' is not placed in a cell."
                        );
                    }

                    // A move that runs again after its commit's acknowledgement is lost finds the tenant in the
                    // target cell, which the route allows only as the caller, with the offer consumed.
                    if (
                        string.Equals(tenant.Cell, toCell, StringComparison.Ordinal)
                        && !string.Equals(fromCell, toCell, StringComparison.Ordinal)
                    )
                    {
                        return MoveResult.Moved();
                    }

                    if (!string.Equals(tenant.Cell, fromCell, StringComparison.Ordinal))
                    {
                        return MoveResult.Conflict(
                            $"The tenant '{tenantId}' is not placed in the cell '{fromCell}'."
                        );
                    }

                    if (string.Equals(fromCell, toCell, StringComparison.Ordinal))
                    {
                        return MoveResult.Conflict(
                            $"The tenant '{tenantId}' is already placed in the cell '{toCell}'."
                        );
                    }

                    var from = await dbContext
                        .Cells.AsNoTracking()
                        .SingleOrDefaultAsync(entity => entity.Name == fromCell, cancellationToken);
                    if (from is null)
                    {
                        return MoveResult.Conflict($"The cell '{fromCell}' is not registered.");
                    }

                    var to = await dbContext
                        .Cells.AsNoTracking()
                        .SingleOrDefaultAsync(entity => entity.Name == toCell, cancellationToken);
                    if (to is null)
                    {
                        return MoveResult.Conflict($"The cell '{toCell}' is not registered.");
                    }

                    if (!string.Equals(from.Region, to.Region, StringComparison.Ordinal))
                    {
                        return MoveResult.Conflict(
                            $"The cells '{fromCell}' and '{toCell}' are in different regions, and a tenant moves only within its region."
                        );
                    }

                    var offer = await dbContext.TenantMoveOffers.SingleOrDefaultAsync(
                        entity => entity.NormalizedTenantId == normalizedTenantId,
                        cancellationToken
                    );
                    if (
                        offer is null
                        || !string.Equals(offer.FromCell, fromCell, StringComparison.Ordinal)
                        || !string.Equals(offer.ToCell, toCell, StringComparison.Ordinal)
                    )
                    {
                        return MoveResult.Conflict(
                            $"The cell '{fromCell}' has not offered the tenant '{tenantId}' to the cell '{toCell}'."
                        );
                    }

                    var now = TimeProvider.GetUtcNow();
                    if (offer.ExpiresWhen <= now)
                    {
                        return MoveResult.Conflict(
                            $"The offer of the tenant '{tenantId}' to the cell '{toCell}' has expired."
                        );
                    }

                    dbContext.TenantMoveOffers.Remove(offer);
                    tenant.Cell = toCell;
                    tenant.ModifiedWhen = now;

                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return MoveResult.Moved();
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    async ValueTask<MoveOfferResult> ITenantDirectory.OfferMoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async _ =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken
                    );

                    var normalizedTenantId = Normalize(tenantId);
                    var tenant = await dbContext
                        .Tenants.AsNoTracking()
                        .SingleOrDefaultAsync(
                            entity =>
                                entity.NormalizedTenantId == normalizedTenantId
                                && entity.State == TenantRecord.StatePlaced,
                            cancellationToken
                        );
                    if (tenant is null)
                    {
                        return MoveOfferResult.Conflict(
                            $"The tenant '{tenantId}' is not placed in a cell."
                        );
                    }

                    if (!string.Equals(tenant.Cell, fromCell, StringComparison.Ordinal))
                    {
                        return MoveOfferResult.Conflict(
                            $"The tenant '{tenantId}' is not placed in the cell '{fromCell}'."
                        );
                    }

                    if (string.Equals(fromCell, toCell, StringComparison.Ordinal))
                    {
                        return MoveOfferResult.Conflict(
                            $"The tenant '{tenantId}' is already placed in the cell '{toCell}'."
                        );
                    }

                    var from = await dbContext
                        .Cells.AsNoTracking()
                        .SingleOrDefaultAsync(entity => entity.Name == fromCell, cancellationToken);
                    if (from is null)
                    {
                        return MoveOfferResult.Conflict(
                            $"The cell '{fromCell}' is not registered."
                        );
                    }

                    var to = await dbContext
                        .Cells.AsNoTracking()
                        .SingleOrDefaultAsync(entity => entity.Name == toCell, cancellationToken);
                    if (to is null)
                    {
                        return MoveOfferResult.Conflict($"The cell '{toCell}' is not registered.");
                    }

                    if (!string.Equals(from.Region, to.Region, StringComparison.Ordinal))
                    {
                        return MoveOfferResult.Conflict(
                            $"The cells '{fromCell}' and '{toCell}' are in different regions, and a tenant moves only within its region."
                        );
                    }

                    var now = TimeProvider.GetUtcNow();
                    var offer = await dbContext.TenantMoveOffers.SingleOrDefaultAsync(
                        entity => entity.NormalizedTenantId == normalizedTenantId,
                        cancellationToken
                    );
                    if (offer is null)
                    {
                        dbContext.TenantMoveOffers.Add(
                            new TenantMoveOfferRecord
                            {
                                NormalizedTenantId = normalizedTenantId,
                                FromCell = fromCell,
                                ToCell = toCell,
                                CreatedWhen = now,
                                ExpiresWhen = now.AddHours(MoveOfferLifetimeHours),
                            }
                        );
                    }
                    else
                    {
                        offer.FromCell = fromCell;
                        offer.ToCell = toCell;
                        offer.CreatedWhen = now;
                        offer.ExpiresWhen = now.AddHours(MoveOfferLifetimeHours);
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return MoveOfferResult.Offered();
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    async ValueTask ITenantDirectory.RetireAsync(
        TenantRetirement retirement,
        CancellationToken cancellationToken
    )
    {
        // A write through ITenantDirectory is the directory's own and carries every cell's authority.
        await RetireCoreAsync(retirement, callerCell: null, cancellationToken);
    }

    /// <summary>
    /// Retires a tenant in a serializable transaction. The tenant's record becomes retired and holds no issuer, and the
    /// issuer that the tenant held, or that the retirement names when the tenant has no record, is recorded as retired
    /// so that no other tenant takes it.
    /// </summary>
    /// <param name="retirement">The <see cref="TenantRetirement"/> that names the tenant to retire.</param>
    /// <param name="callerCell">The name of the calling cell, or <c>null</c> for the directory's own call.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A forbidden result when the caller's cell does not hold the tenant and is not the control-plane cell; otherwise,
    /// a done result, including when the tenant is already retired.
    /// </returns>
    private async ValueTask<DirectoryTenantWriteResult> RetireCoreAsync(
        TenantRetirement retirement,
        string? callerCell,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async _ =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken
                    );

                    var tenantId = retirement.TenantId;
                    var normalizedTenantId = Normalize(tenantId);
                    var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                        entity => entity.NormalizedTenantId == normalizedTenantId,
                        cancellationToken
                    );
                    if (!IsAuthorized(callerCell, tenant?.Cell))
                    {
                        return DirectoryTenantWriteResult.Forbidden(WriteForbiddenDetail);
                    }

                    var now = TimeProvider.GetUtcNow();
                    string? issuer;
                    if (tenant is null)
                    {
                        // A tenant with no record holds no issuer the directory knows, so the retirement names it.
                        issuer = retirement.Issuer;
                        dbContext.Tenants.Add(
                            new TenantRecord
                            {
                                TenantId = tenantId,
                                NormalizedTenantId = normalizedTenantId,
                                State = TenantRecord.StateRetired,
                                CreatedWhen = now,
                                ModifiedWhen = now,
                            }
                        );
                    }
                    else if (tenant.State == TenantRecord.StateRetired)
                    {
                        return DirectoryTenantWriteResult.Done();
                    }
                    else
                    {
                        // The issuer the tenant holds is retired, so no other tenant can take it, whatever the
                        // retirement names. A retired record holds no issuer: the issuer is recorded as retired, below.
                        issuer = tenant.Issuer;
                        tenant.State = TenantRecord.StateRetired;
                        tenant.Issuer = null;
                        tenant.NormalizedIssuer = null;
                        tenant.NormalizedTeamId = null;
                        tenant.ModifiedWhen = now;
                    }

                    if (!string.IsNullOrWhiteSpace(issuer))
                    {
                        await AddRetiredIssuerAsync(dbContext, tenantId, issuer, cancellationToken);
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return DirectoryTenantWriteResult.Done();
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    async ValueTask<ReservationResult> ITenantDirectory.ChangeIssuerAsync(
        TenantIssuerChange change,
        CancellationToken cancellationToken
    )
    {
        // A write through ITenantDirectory is the directory's own and carries every cell's authority.
        var result = await ChangeIssuerCoreAsync(change, callerCell: null, cancellationToken);
        return result.Outcome == DirectoryTenantWriteOutcome.Done
            ? ReservationResult.Reserved()
            : ReservationResult.Conflict(result.Detail ?? string.Empty);
    }

    /// <summary>
    /// Changes the issuer of a tenant in a serializable transaction. The tenant must hold the previous issuer, the new
    /// issuer must be held by no other tenant and must not belong to a retired holder, and the issuer that the tenant
    /// gives up is recorded as retired.
    /// </summary>
    /// <param name="change">The <see cref="TenantIssuerChange"/> that names the tenant and its issuers.</param>
    /// <param name="callerCell">The name of the calling cell, or <c>null</c> for the directory's own call.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A forbidden result when the caller's cell does not hold the tenant and is not the control-plane cell; a
    /// not-found result when the tenant has no record; a conflict result when the tenant is retired, does not hold the
    /// previous issuer or the new issuer is taken; otherwise, a done result, including when the tenant already holds
    /// the new issuer.
    /// </returns>
    private async ValueTask<DirectoryTenantWriteResult> ChangeIssuerCoreAsync(
        TenantIssuerChange change,
        string? callerCell,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async _ =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken
                    );

                    var tenantId = change.TenantId;
                    var normalizedTenantId = Normalize(tenantId);
                    var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                        entity => entity.NormalizedTenantId == normalizedTenantId,
                        cancellationToken
                    );
                    if (!IsAuthorized(callerCell, tenant?.Cell))
                    {
                        return DirectoryTenantWriteResult.Forbidden(WriteForbiddenDetail);
                    }

                    if (tenant is null)
                    {
                        return DirectoryTenantWriteResult.NotFound(
                            $"The tenant '{tenantId}' has no record."
                        );
                    }

                    if (tenant.State == TenantRecord.StateRetired)
                    {
                        return DirectoryTenantWriteResult.Conflict(
                            $"The tenant '{tenantId}' is retired."
                        );
                    }

                    var storedIssuer = tenant.NormalizedIssuer;
                    var newIssuer = change.Issuer is { } issuerValue
                        ? Normalize(issuerValue)
                        : null;
                    if (string.Equals(storedIssuer, newIssuer, StringComparison.Ordinal))
                    {
                        // The tenant already holds the new issuer, so a retried change is done.
                        return DirectoryTenantWriteResult.Done();
                    }

                    var previousIssuer = change.PreviousIssuer is { } previousValue
                        ? Normalize(previousValue)
                        : null;
                    if (!string.Equals(storedIssuer, previousIssuer, StringComparison.Ordinal))
                    {
                        return DirectoryTenantWriteResult.Conflict(
                            $"The tenant '{tenantId}' does not hold the issuer '{change.PreviousIssuer}'."
                        );
                    }

                    if (change.Issuer is { } issuer)
                    {
                        if (
                            await dbContext.Tenants.AnyAsync(
                                entity =>
                                    entity.NormalizedIssuer == newIssuer
                                    && entity.NormalizedTenantId != normalizedTenantId,
                                cancellationToken
                            )
                        )
                        {
                            return DirectoryTenantWriteResult.Conflict(
                                $"Another tenant already has the issuer '{issuer}'."
                            );
                        }

                        var retiredIssuerDetail = await FindRetiredIssuerConflictAsync(
                            dbContext,
                            tenantId,
                            issuer,
                            cancellationToken
                        );
                        if (retiredIssuerDetail is not null)
                        {
                            return DirectoryTenantWriteResult.Conflict(retiredIssuerDetail);
                        }
                    }

                    if (tenant.Issuer is { } heldIssuer)
                    {
                        await AddRetiredIssuerAsync(
                            dbContext,
                            tenantId,
                            heldIssuer,
                            cancellationToken
                        );
                    }

                    tenant.Issuer = change.Issuer;
                    tenant.NormalizedIssuer = newIssuer;
                    tenant.ModifiedWhen = TimeProvider.GetUtcNow();

                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return DirectoryTenantWriteResult.Done();
                },
                cancellationToken
            );
    }

    /// <summary>
    /// Gets whether a cell is authorized to write a tenant: the call is the directory's own, the cell holds the tenant
    /// or the cell is the control-plane cell. A tenant with no record, or a record that names no cell, is held by no
    /// cell.
    /// </summary>
    /// <param name="callerCell">
    /// The name of the cell that makes the call, or <c>null</c> for the directory's own call.
    /// </param>
    /// <param name="storedCell">The name of the cell that the tenant's record names, or <c>null</c>.</param>
    /// <returns><c>true</c> when the call is authorized; otherwise, <c>false</c>.</returns>
    private bool IsAuthorized(string? callerCell, string? storedCell) =>
        callerCell is null
        || string.Equals(callerCell, storedCell, StringComparison.Ordinal)
        || string.Equals(
            callerCell,
            ServerOptions.Value.ControlPlaneCell,
            StringComparison.Ordinal
        );

    /// <inheritdoc />
    async ValueTask<TenantPlacement?> ITenantDirectory.GetPlacementOrDefaultAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTenantId = Normalize(tenantId);
        var tenant = await dbContext
            .Tenants.AsNoTracking()
            .Where(entity =>
                entity.NormalizedTenantId == normalizedTenantId
                && entity.State == TenantRecord.StatePlaced
            )
            .Select(entity => new
            {
                entity.TenantId,
                entity.Issuer,
                entity.Cell,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (tenant?.Cell is not { } cell)
        {
            return null;
        }

        var region = await dbContext
            .Cells.AsNoTracking()
            .Where(entity => entity.Name == cell)
            .Select(entity => entity.Region)
            .SingleOrDefaultAsync(cancellationToken);

        return new TenantPlacement
        {
            TenantId = tenant.TenantId,
            Issuer = tenant.Issuer ?? string.Empty,
            Cell = cell,
            Region = region ?? string.Empty,
        };
    }

    /// <inheritdoc />
    async ValueTask<bool> ITenantDirectory.IsTenantIdRetiredAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTenantId = Normalize(tenantId);
        return await dbContext.Tenants.AnyAsync(
            entity =>
                entity.NormalizedTenantId == normalizedTenantId
                && entity.State == TenantRecord.StateRetired,
            cancellationToken
        );
    }

    /// <inheritdoc />
    async ValueTask<string?> ITenantDirectory.GetRetiredIssuerHolderOrDefaultAsync(
        string issuer,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        return await GetRetiredIssuerHolderCoreAsync(
            dbContext,
            Normalize(issuer),
            cancellationToken
        );
    }

    /// <summary>
    /// Finds whether an issuer is retired by a tenant other than the specified one. Relying parties that trusted a
    /// retired issuer would accept another tenant's tokens under it, so only the tenant that gave it up can take it
    /// back.
    /// </summary>
    /// <param name="dbContext">The <see cref="DirectoryDbContext"/> to read from.</param>
    /// <param name="tenantId">The identifier of the tenant that would hold the issuer.</param>
    /// <param name="issuer">The issuer that the tenant would hold.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The conflict detail when another tenant retired the issuer; otherwise, <c>null</c>.</returns>
    private static async ValueTask<string?> FindRetiredIssuerConflictAsync(
        DirectoryDbContext dbContext,
        string tenantId,
        string issuer,
        CancellationToken cancellationToken
    )
    {
        var formerHolder = await GetRetiredIssuerHolderCoreAsync(
            dbContext,
            Normalize(issuer),
            cancellationToken
        );
        return
            formerHolder is not null
            && !string.Equals(formerHolder, tenantId, StringComparison.OrdinalIgnoreCase)
            ? $"The issuer '{issuer}' belonged to another tenant and is never reissued."
            : null;
    }

    /// <summary>
    /// Gets the tenant that retired an issuer.
    /// </summary>
    /// <param name="dbContext">The <see cref="DirectoryDbContext"/> to read from.</param>
    /// <param name="normalizedIssuer">The normalized issuer to look up.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The identifier of the tenant that retired the issuer, or <c>null</c> when no tenant retired it.
    /// </returns>
    private static async ValueTask<string?> GetRetiredIssuerHolderCoreAsync(
        DirectoryDbContext dbContext,
        string normalizedIssuer,
        CancellationToken cancellationToken
    )
    {
        return await dbContext
            .RetiredIssuers.AsNoTracking()
            .Where(entity => entity.NormalizedIssuer == normalizedIssuer)
            .Select(entity => entity.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Records that a tenant gave up an issuer, unless it is already retired, so the first tenant to retire an issuer
    /// stays its holder.
    /// </summary>
    /// <param name="dbContext">The <see cref="DirectoryDbContext"/> to write to.</param>
    /// <param name="tenantId">The identifier of the tenant that gave up the issuer.</param>
    /// <param name="issuer">The issuer that the tenant gave up.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    private static async ValueTask AddRetiredIssuerAsync(
        DirectoryDbContext dbContext,
        string tenantId,
        string issuer,
        CancellationToken cancellationToken
    )
    {
        var normalizedIssuer = Normalize(issuer);
        if (
            await dbContext.RetiredIssuers.AnyAsync(
                entity => entity.NormalizedIssuer == normalizedIssuer,
                cancellationToken
            )
        )
        {
            return;
        }

        dbContext.RetiredIssuers.Add(
            new RetiredIssuerRecord
            {
                Issuer = issuer,
                NormalizedIssuer = normalizedIssuer,
                TenantId = tenantId,
            }
        );
    }

    /// <summary>
    /// Finds a team of the lineage of the specified team that already holds the most tenants its limit allows. A team's
    /// limit is the lesser of the platform document's <c>max_tenants</c> setting and the team document's own, and it
    /// bounds the reserved and placed tenants of the team and of every team beneath it. The lineage is walked through
    /// the parent identifiers of the team documents, deepest team first.
    /// </summary>
    /// <param name="dbContext">The <see cref="DirectoryDbContext"/> to read from.</param>
    /// <param name="teamId">The identifier of the team that would hold the new tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The conflict detail when the team has no record, a team of the lineage is full or a <c>max_tenants</c> setting
    /// cannot be read; otherwise, <c>null</c>. A team above the first whose record is missing ends the walk.
    /// </returns>
    // See ADR-1411.
    // See ADR-1426.
    private async ValueTask<string?> FindTeamAtLimitAsync(
        DirectoryDbContext dbContext,
        string teamId,
        CancellationToken cancellationToken
    )
    {
        var platformJson = await dbContext
            .Platforms.AsNoTracking()
            .Select(entity => entity.DocumentJson)
            .SingleOrDefaultAsync(cancellationToken);
        if (
            !TryReadMaxTenants<PlatformDocument>(
                platformJson,
                document => document.Settings,
                out var platformLimit
            )
        )
        {
            return "The platform has a max_tenants setting the directory cannot read.";
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? nextTeamId = Normalize(teamId);
        var isNamedTeam = true;
        while (nextTeamId is not null && visited.Add(nextTeamId))
        {
            var currentTeamId = nextTeamId;
            var team = await dbContext
                .Teams.AsNoTracking()
                .Where(entity => entity.NormalizedTeamId == currentTeamId)
                .Select(entity => new
                {
                    entity.TeamId,
                    entity.NormalizedParentTeamId,
                    entity.NormalizedPath,
                    entity.DocumentJson,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (team is null)
            {
                // A team's row exists from its own reservation until it is released, so a named team without a row
                // is not known to the directory and its limits cannot be read.
                return isNamedTeam ? $"The team '{teamId}' has no record." : null;
            }

            isNamedTeam = false;
            nextTeamId = team.NormalizedParentTeamId;

            if (
                !TryReadMaxTenants<TeamDocument>(
                    team.DocumentJson,
                    document => document.Settings,
                    out var ownLimit
                )
            )
            {
                return $"The team '{team.TeamId}' has a max_tenants setting the directory cannot read.";
            }

            var effectiveLimit = TenantLimitRules.GetEffectiveLimit(platformLimit, ownLimit);
            if (effectiveLimit is not { } maxTenants)
            {
                continue;
            }

            var teamPath = team.NormalizedPath;
            var count = await dbContext
                .Tenants.Where(tenant =>
                    tenant.State != TenantRecord.StateRetired
                    && dbContext.Teams.Any(subtreeTeam =>
                        subtreeTeam.NormalizedTeamId == tenant.NormalizedTeamId
                        && subtreeTeam.NormalizedPath.StartsWith(teamPath)
                    )
                )
                .LongCountAsync(cancellationToken);
            if (count >= maxTenants)
            {
                return $"The team '{team.TeamId}' holds its maximum of {maxTenants} tenants.";
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the normalized team path of a team: the path of its parent followed by the team's own normalized identifier
    /// and <c>/</c>. A team with no parent is a top-level team. A team whose parent the directory does not hold is
    /// placed top-level until its parent is published, which re-paths it and its subtree beneath the parent.
    /// </summary>
    // See ADR-1426.
    private static async ValueTask<string> GetTeamPathAsync(
        DirectoryDbContext dbContext,
        string normalizedTeamId,
        string? normalizedParentTeamId,
        CancellationToken cancellationToken
    )
    {
        var parentPath = normalizedParentTeamId is null
            ? null
            : await dbContext
                .Teams.AsNoTracking()
                .Where(entity => entity.NormalizedTeamId == normalizedParentTeamId)
                .Select(entity => entity.NormalizedPath)
                .SingleOrDefaultAsync(cancellationToken);

        return $"{parentPath ?? "/"}{normalizedTeamId}/";
    }

    /// <summary>
    /// Re-paths the published children of a team that were placed top-level because the directory did not hold the team
    /// when they were published, and the subtrees beneath those children, so each path lies beneath the team's path.
    /// Every new path is computed first, and none is assigned when one would be deeper than the directory stores.
    /// </summary>
    /// <param name="dbContext">The <see cref="DirectoryDbContext"/> that tracks the changes.</param>
    /// <param name="normalizedTeamId">The normalized identifier of the team that is published.</param>
    /// <param name="teamPath">The normalized path of the team that is published.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The identifier of a team that the re-pathing would nest deeper than the directory stores, or <c>null</c> when
    /// every orphaned child and its subtree is re-pathed.
    /// </returns>
    // See ADR-1426.
    private static async ValueTask<string?> RepathOrphanedChildrenAsync(
        DirectoryDbContext dbContext,
        string normalizedTeamId,
        string teamPath,
        CancellationToken cancellationToken
    )
    {
        // A child published before its parent is re-pathed when the parent is published.
        var children = await dbContext
            .Teams.Where(entity =>
                entity.NormalizedParentTeamId == normalizedTeamId
                && entity.NormalizedPath != teamPath + entity.NormalizedTeamId + "/"
            )
            .ToListAsync(cancellationToken);

        var repaths = new List<(TeamRecord Team, string NormalizedPath)>();
        foreach (var child in children)
        {
            var oldPrefix = child.NormalizedPath;
            var newPrefix = $"{teamPath}{child.NormalizedTeamId}/";

            // The parent lies in the child's subtree, so the lineage is a cycle that has no path beneath the parent.
            if (teamPath.StartsWith(oldPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var subtree = await dbContext
                .Teams.Where(entity => entity.NormalizedPath.StartsWith(oldPrefix))
                .ToListAsync(cancellationToken);
            foreach (var subtreeTeam in subtree)
            {
                var newPath = newPrefix + subtreeTeam.NormalizedPath[oldPrefix.Length..];
                if (GetTeamDepth(newPath) > DirectoryLimits.MaxTeamDepth)
                {
                    return subtreeTeam.TeamId;
                }

                repaths.Add((subtreeTeam, newPath));
            }
        }

        foreach (var (repathedTeam, newPath) in repaths)
        {
            repathedTeam.NormalizedPath = newPath;
        }

        return null;
    }

    /// <summary>
    /// Lists every published team beneath a team again after the team's own change, in path order with each parent
    /// before its children, so a team's change always follows the latest change of each of its ancestors: each gets a
    /// change with the next sequence that carries its document unchanged, its previous change is deleted, and its row
    /// takes the new sequence.
    /// </summary>
    /// <param name="dbContext">The <see cref="DirectoryDbContext"/> that tracks the changes.</param>
    /// <param name="teamPath">The normalized path of the team that is published.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    // See ADR-1411.
    private static async ValueTask ResequenceDescendantsAsync(
        DirectoryDbContext dbContext,
        string teamPath,
        CancellationToken cancellationToken
    )
    {
        var descendants = await dbContext
            .Teams.Where(entity =>
                entity.State == TeamRecord.StatePublished
                && entity.NormalizedPath != teamPath
                && entity.NormalizedPath.StartsWith(teamPath)
            )
            .ToListAsync(cancellationToken);
        if (descendants.Count == 0)
        {
            return;
        }

        var previousSequences = descendants.ConvertAll(team => team.Sequence);
        await dbContext
            .Changes.Where(entity => previousSequences.Contains(entity.Sequence))
            .ExecuteDeleteAsync(cancellationToken);

        // A path is a prefix of its descendants' paths, so each parent sorts before its children.
        foreach (
            var descendant in descendants.OrderBy(
                team => team.NormalizedPath,
                StringComparer.Ordinal
            )
        )
        {
            var change = new DirectoryChangeRecord
            {
                Sequence = await NextSequenceAsync(dbContext, cancellationToken),
                TeamId = descendant.TeamId,
                DocumentJson = descendant.DocumentJson,
            };
            dbContext.Changes.Add(change);
            descendant.Sequence = change.Sequence;
        }
    }

    /// <summary>
    /// Counts the teams of a normalized team path, so <c>/a/b/</c> holds two.
    /// </summary>
    /// <param name="normalizedPath">The normalized path of a team.</param>
    /// <returns>The number of teams on the path.</returns>
    private static int GetTeamDepth(string normalizedPath) =>
        normalizedPath.AsSpan().Count('/') - 1;

    /// <summary>
    /// Describes why the directory refuses a team that would be nested too deep.
    /// </summary>
    /// <param name="teamId">The identifier of the team that would be nested too deep.</param>
    /// <returns>The reason for the caller.</returns>
    private static string GetTeamTooDeepDetail(string teamId) =>
        $"The team '{teamId}' cannot be nested deeper than {DirectoryLimits.MaxTeamDepth} levels.";

    /// <summary>
    /// Reads the <c>max_tenants</c> setting of a platform or team document.
    /// </summary>
    /// <param name="documentJson">The stored envelope of the document, or <c>null</c> when none is stored.</param>
    /// <param name="getSettings">Gets the settings object of the deserialized document.</param>
    /// <param name="maxTenants">The limit, or <c>null</c> when the document has no <c>max_tenants</c> setting.</param>
    /// <returns>
    /// <c>true</c> when no document is stored, or its settings are a JSON object whose setting is absent or a
    /// non-negative integer; otherwise, <c>false</c> because the document, its settings or the setting cannot be read.
    /// </returns>
    private bool TryReadMaxTenants<TDocument>(
        string? documentJson,
        Func<TDocument, JsonElement> getSettings,
        out int? maxTenants
    )
    {
        maxTenants = null;
        if (documentJson is null)
        {
            return true;
        }

        try
        {
            var envelope = JsonElement.Parse(documentJson);
            var document = PayloadSerializer.Deserialize<TDocument>(envelope, JsonOptions);
            var settings = getSettings(document);

            return TenantLimitRules.TryReadMaxTenants(settings, out maxTenants);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    #endregion

    #region IDirectoryTenantWriter

    /// <inheritdoc />
    async ValueTask<DirectoryTenantWriteResult> IDirectoryTenantWriter.ReleaseAsync(
        string tenantId,
        string callerCell,
        CancellationToken cancellationToken
    ) => await ReleaseCoreAsync(tenantId, callerCell, cancellationToken);

    /// <inheritdoc />
    async ValueTask<DirectoryTenantWriteResult> IDirectoryTenantWriter.RetireAsync(
        TenantRetirement retirement,
        string callerCell,
        CancellationToken cancellationToken
    ) => await RetireCoreAsync(retirement, callerCell, cancellationToken);

    /// <inheritdoc />
    async ValueTask<DirectoryTenantWriteResult> IDirectoryTenantWriter.ChangeIssuerAsync(
        TenantIssuerChange change,
        string callerCell,
        CancellationToken cancellationToken
    ) => await ChangeIssuerCoreAsync(change, callerCell, cancellationToken);

    #endregion

    #region IDirectoryTeamWriter

    /// <inheritdoc />
    async ValueTask<DirectoryTeamWriteResult> IDirectoryTeamWriter.PublishAsync(
        TeamDocument document,
        CancellationToken cancellationToken
    ) => await PublishTeamCoreAsync(document, cancellationToken);

    #endregion

    #region ITeamDirectory

    /// <inheritdoc />
    async ValueTask<ReservationResult> ITeamDirectory.ReserveAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async _ =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken
                    );

                    var normalizedTeamId = Normalize(teamId);
                    var existingState = await dbContext
                        .Teams.AsNoTracking()
                        .Where(entity => entity.NormalizedTeamId == normalizedTeamId)
                        .Select(entity => entity.State)
                        .SingleOrDefaultAsync(cancellationToken);
                    if (existingState == TeamRecord.StateRetired)
                    {
                        return ReservationResult.Conflict(
                            $"The team identifier '{teamId}' belonged to a deleted team and is never reissued."
                        );
                    }

                    if (existingState == TeamRecord.StateReserved)
                    {
                        // Only the control-plane cell reserves a team, so a reservation that no publish has consumed
                        // is the caller's own, which a reservation that runs again after its commit's acknowledgement
                        // is lost finds.
                        return ReservationResult.Reserved();
                    }

                    if (existingState is not null)
                    {
                        return ReservationResult.Conflict(
                            $"Another team already has the identifier '{teamId}'."
                        );
                    }

                    dbContext.Teams.Add(
                        new TeamRecord
                        {
                            TeamId = teamId,
                            NormalizedTeamId = normalizedTeamId,
                            NormalizedPath = await GetTeamPathAsync(
                                dbContext,
                                normalizedTeamId,
                                null,
                                cancellationToken
                            ),
                            State = TeamRecord.StateReserved,
                        }
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return ReservationResult.Reserved();
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The document is refused because a team would be nested deeper than the directory stores.
    /// </exception>
    async ValueTask ITeamDirectory.PublishAsync(
        TeamDocument document,
        CancellationToken cancellationToken
    )
    {
        var result = await PublishTeamCoreAsync(document, cancellationToken);
        if (!result.IsDone)
        {
            throw new InvalidOperationException(result.Detail);
        }
    }

    /// <summary>
    /// Publishes a team document in a serializable transaction. A retired identifier has nothing to publish, and a
    /// document whose team, or a team beneath it, would be nested deeper than the directory stores is refused before
    /// the transaction commits, so nothing is stored.
    /// </summary>
    /// <param name="document">The <see cref="TeamDocument"/> to publish.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A refused result naming the team that would be too deep; otherwise, a done result.</returns>
    // See ADR-1426.
    private async ValueTask<DirectoryTeamWriteResult> PublishTeamCoreAsync(
        TeamDocument document,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async token =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        token
                    );

                    var normalizedTeamId = Normalize(document.TeamId);
                    var team = await dbContext.Teams.SingleOrDefaultAsync(
                        entity => entity.NormalizedTeamId == normalizedTeamId,
                        token
                    );
                    if (team is { State: TeamRecord.StateRetired })
                    {
                        // A retired identifier is never reissued, so a document cannot bring it back.
                        return DirectoryTeamWriteResult.Done();
                    }

                    var normalizedParentTeamId = document.ParentTeamId is { } parentTeamId
                        ? Normalize(parentTeamId)
                        : null;

                    // A published team keeps its path; publishing its parent re-paths it when it was placed before
                    // its parent.
                    var teamPath = team is { State: TeamRecord.StatePublished }
                        ? team.NormalizedPath
                        : await GetTeamPathAsync(
                            dbContext,
                            normalizedTeamId,
                            normalizedParentTeamId,
                            token
                        );

                    // A path deeper than the directory stores is refused before anything is written.
                    if (GetTeamDepth(teamPath) > DirectoryLimits.MaxTeamDepth)
                    {
                        return DirectoryTeamWriteResult.Refused(
                            GetTeamTooDeepDetail(document.TeamId)
                        );
                    }

                    // The directory assigns the sequence, so the stored document carries none: it is read back from the
                    // record.
                    var documentJson = PayloadSerializer
                        .Serialize(document with { Sequence = 0 }, JsonOptions)
                        .GetRawText();
                    var change = new DirectoryChangeRecord
                    {
                        Sequence = await NextSequenceAsync(dbContext, token),
                        TeamId = document.TeamId,
                        DocumentJson = documentJson,
                    };

                    // A change carries the team's whole document, so the feed keeps only the team's latest change,
                    // whose sequence the team row holds.
                    if (team is { Sequence: > 0 })
                    {
                        var previousSequence = team.Sequence;
                        await dbContext
                            .Changes.Where(entity => entity.Sequence == previousSequence)
                            .ExecuteDeleteAsync(token);
                    }
                    dbContext.Changes.Add(change);
                    await dbContext.SaveChangesAsync(token);

                    if (team is null)
                    {
                        team = new TeamRecord
                        {
                            TeamId = document.TeamId,
                            NormalizedTeamId = normalizedTeamId,
                            NormalizedPath = teamPath,
                            State = TeamRecord.StatePublished,
                        };
                        dbContext.Teams.Add(team);
                    }

                    team.State = TeamRecord.StatePublished;
                    team.NormalizedParentTeamId = normalizedParentTeamId;
                    team.NormalizedPath = teamPath;
                    team.DocumentJson = documentJson;
                    team.Sequence = change.Sequence;

                    var tooDeepTeamId = await RepathOrphanedChildrenAsync(
                        dbContext,
                        normalizedTeamId,
                        teamPath,
                        token
                    );
                    if (tooDeepTeamId is not null)
                    {
                        // Returning before the commit rolls the transaction back, so the change record, the sequence
                        // and the team row are discarded, and no child is re-pathed.
                        return DirectoryTeamWriteResult.Refused(
                            GetTeamTooDeepDetail(tooDeepTeamId)
                        );
                    }

                    // The re-pathed rows are saved before the subtree query reads their new paths.
                    await dbContext.SaveChangesAsync(token);
                    await ResequenceDescendantsAsync(dbContext, teamPath, token);

                    await dbContext.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);

                    return DirectoryTeamWriteResult.Done();
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    async ValueTask ITeamDirectory.ReleaseAsync(string teamId, CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTeamId = Normalize(teamId);
        var team = await dbContext.Teams.SingleOrDefaultAsync(
            entity =>
                entity.NormalizedTeamId == normalizedTeamId
                && entity.State == TeamRecord.StateReserved,
            cancellationToken
        );
        if (team is null)
        {
            return;
        }

        dbContext.Teams.Remove(team);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    async ValueTask ITeamDirectory.RetireAsync(string teamId, CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async token =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        token
                    );

                    var normalizedTeamId = Normalize(teamId);
                    var team = await dbContext.Teams.SingleOrDefaultAsync(
                        entity => entity.NormalizedTeamId == normalizedTeamId,
                        token
                    );
                    if (team is { State: TeamRecord.StateRetired })
                    {
                        return;
                    }

                    var change = new DirectoryChangeRecord
                    {
                        Sequence = await NextSequenceAsync(dbContext, token),
                        TeamId = teamId,
                        DocumentJson = null,
                    };

                    // The retire is the team's last state, so the feed keeps only it in place of the team's latest
                    // change, whose sequence the team row holds.
                    if (team is { Sequence: > 0 })
                    {
                        var previousSequence = team.Sequence;
                        await dbContext
                            .Changes.Where(entity => entity.Sequence == previousSequence)
                            .ExecuteDeleteAsync(token);
                    }
                    dbContext.Changes.Add(change);
                    await dbContext.SaveChangesAsync(token);

                    if (team is null)
                    {
                        team = new TeamRecord
                        {
                            TeamId = teamId,
                            NormalizedTeamId = normalizedTeamId,
                            NormalizedPath = await GetTeamPathAsync(
                                dbContext,
                                normalizedTeamId,
                                null,
                                token
                            ),
                            State = TeamRecord.StateRetired,
                        };
                        dbContext.Teams.Add(team);
                    }

                    team.State = TeamRecord.StateRetired;
                    team.DocumentJson = null;
                    team.Sequence = change.Sequence;

                    await dbContext.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    async ValueTask<bool> ITeamDirectory.IsTeamIdRetiredAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTeamId = Normalize(teamId);
        return await dbContext.Teams.AnyAsync(
            entity =>
                entity.NormalizedTeamId == normalizedTeamId
                && entity.State == TeamRecord.StateRetired,
            cancellationToken
        );
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">
    /// The stored team document is not the schema-versioned envelope of a team document.
    /// </exception>
    async ValueTask<TeamDocument?> ITeamDirectory.GetOrDefaultAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTeamId = Normalize(teamId);
        var team = await dbContext
            .Teams.AsNoTracking()
            .Where(entity =>
                entity.NormalizedTeamId == normalizedTeamId
                && entity.State == TeamRecord.StatePublished
            )
            .Select(entity => new { entity.DocumentJson, entity.Sequence })
            .SingleOrDefaultAsync(cancellationToken);
        if (team?.DocumentJson is not { } documentJson)
        {
            return null;
        }

        var document = PayloadSerializer.Deserialize<TeamDocument>(
            JsonElement.Parse(documentJson),
            JsonOptions
        );
        return document with { Sequence = team.Sequence };
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">
    /// A stored change is not the schema-versioned envelope of its document.
    /// </exception>
    async ValueTask<IReadOnlyList<DirectoryChange>> ITeamDirectory.ListChangesAsync(
        long after,
        int limit,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var records = await dbContext
            .Changes.AsNoTracking()
            .Where(entity => entity.Sequence > after)
            .OrderBy(entity => entity.Sequence)
            .Take(Math.Max(limit, 0))
            .ToListAsync(cancellationToken);

        var changes = new List<DirectoryChange>(records.Count);
        foreach (var record in records)
        {
            if (record.TeamId is null)
            {
                changes.Add(
                    new DirectoryChange
                    {
                        Sequence = record.Sequence,
                        Platform = ReadPlatformDocument(record.DocumentJson, record.Sequence),
                    }
                );
                continue;
            }

            var document = record.DocumentJson is null
                ? null
                : PayloadSerializer.Deserialize<TeamDocument>(
                    JsonElement.Parse(record.DocumentJson),
                    JsonOptions
                );
            changes.Add(
                new DirectoryChange
                {
                    Sequence = record.Sequence,
                    TeamId = record.TeamId,
                    Document = document is null
                        ? null
                        : document with
                        {
                            Sequence = record.Sequence,
                        },
                }
            );
        }

        return changes;
    }

    #endregion

    #region ICellDirectory

    /// <inheritdoc />
    async ValueTask ICellDirectory.RegisterAsync(
        CellDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async _ =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken
                    );

                    var cell = await dbContext.Cells.SingleOrDefaultAsync(
                        entity => entity.Name == descriptor.Name,
                        cancellationToken
                    );
                    if (cell is null)
                    {
                        dbContext.Cells.Add(
                            new CellRecord
                            {
                                Name = descriptor.Name,
                                Region = descriptor.Region,
                                Kind = descriptor.Kind,
                                ManagementUrl = descriptor.ManagementUrl,
                                IsControlPlane = descriptor.IsControlPlane,
                            }
                        );
                    }
                    else
                    {
                        cell.Region = descriptor.Region;
                        cell.Kind = descriptor.Kind;
                        cell.ManagementUrl = descriptor.ManagementUrl;
                        cell.IsControlPlane = descriptor.IsControlPlane;
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    async ValueTask<IReadOnlyList<CellDescriptor>> ICellDirectory.ListAsync(
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var records = await dbContext
            .Cells.AsNoTracking()
            .OrderBy(entity => entity.Name)
            .ToListAsync(cancellationToken);
        return [.. records.Select(ToDescriptor)];
    }

    /// <inheritdoc />
    async ValueTask<CellDescriptor?> ICellDirectory.GetOrDefaultAsync(
        string name,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var record = await dbContext
            .Cells.AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Name == name, cancellationToken);
        return record is null ? null : ToDescriptor(record);
    }

    /// <summary>
    /// Projects a stored cell record into the descriptor that the directory publishes.
    /// </summary>
    /// <param name="record">The <see cref="CellRecord"/> to project.</param>
    /// <returns>The <see cref="CellDescriptor"/> of the cell that <paramref name="record"/> stores.</returns>
    private static CellDescriptor ToDescriptor(CellRecord record) =>
        new()
        {
            Name = record.Name,
            Region = record.Region,
            Kind = record.Kind,
            ManagementUrl = record.ManagementUrl,
            IsControlPlane = record.IsControlPlane,
        };

    #endregion

    #region IPlatformDirectory

    /// <inheritdoc />
    async ValueTask IPlatformDirectory.PublishAsync(
        PlatformDocument document,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async token =>
                {
                    // A retried attempt starts from the database, not from the failed attempt's tracked entities.
                    dbContext.ChangeTracker.Clear();
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        token
                    );

                    // The directory assigns the sequence, so the stored document carries none: it is read back from the
                    // record.
                    var documentJson = PayloadSerializer
                        .Serialize(document with { Sequence = 0 }, JsonOptions)
                        .GetRawText();
                    var change = new DirectoryChangeRecord
                    {
                        Sequence = await NextSequenceAsync(dbContext, token),
                        TeamId = null,
                        DocumentJson = documentJson,
                    };

                    // A change carries the platform's whole document, so the feed keeps only the platform's latest
                    // change.
                    await dbContext
                        .Changes.Where(entity => entity.TeamId == null)
                        .ExecuteDeleteAsync(token);
                    dbContext.Changes.Add(change);
                    await dbContext.SaveChangesAsync(token);

                    var platform = await dbContext.Platforms.SingleOrDefaultAsync(
                        entity => entity.Id == PlatformRecord.SingletonId,
                        token
                    );
                    if (platform is null)
                    {
                        dbContext.Platforms.Add(
                            new PlatformRecord
                            {
                                DocumentJson = documentJson,
                                Sequence = change.Sequence,
                            }
                        );
                    }
                    else
                    {
                        platform.DocumentJson = documentJson;
                        platform.Sequence = change.Sequence;
                    }

                    await dbContext.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                },
                cancellationToken
            );
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">
    /// The stored platform document is not the schema-versioned envelope of a platform document.
    /// </exception>
    async ValueTask<PlatformDocument?> IPlatformDirectory.GetOrDefaultAsync(
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var platform = await dbContext
            .Platforms.AsNoTracking()
            .Select(entity => new { entity.DocumentJson, entity.Sequence })
            .SingleOrDefaultAsync(cancellationToken);
        return platform is null
            ? null
            : ReadPlatformDocument(platform.DocumentJson, platform.Sequence);
    }

    /// <summary>
    /// Reads the platform document from its stored envelope and stamps it with the sequence number of its change.
    /// </summary>
    /// <param name="documentJson">
    /// The stored envelope of the platform document, or <c>null</c> when none is stored.
    /// </param>
    /// <param name="sequence">The sequence number of the change that stored the document.</param>
    /// <returns>
    /// The <see cref="PlatformDocument"/> with its <see cref="PlatformDocument.Sequence"/> set, or <c>null</c> when no
    /// document is stored.
    /// </returns>
    /// <exception cref="JsonException">
    /// The stored text is not the schema-versioned envelope of a platform document.
    /// </exception>
    private PlatformDocument? ReadPlatformDocument(string? documentJson, long sequence)
    {
        if (documentJson is null)
        {
            return null;
        }

        var document = PayloadSerializer.Deserialize<PlatformDocument>(
            JsonElement.Parse(documentJson),
            JsonOptions
        );
        return document with { Sequence = sequence };
    }

    #endregion
}
