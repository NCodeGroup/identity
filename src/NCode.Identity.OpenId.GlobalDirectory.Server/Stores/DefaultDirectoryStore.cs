// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    TimeProvider timeProvider
) : ITenantDirectory, ITeamDirectory, ICellDirectory, IPlatformDirectory
{
    // The name of the 'max_tenants' team and platform setting (OpenIdSettingNames.MaxTenants). The directory does not
    // reference the OpenID abstractions that declare it.
    private const string MaxTenantsSettingName = "max_tenants";

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private IDbContextFactory<DirectoryDbContext> DbContextFactory { get; } = dbContextFactory;

    private TimeProvider TimeProvider { get; } = timeProvider;

    private static string Normalize(string value) => value.ToLowerInvariant();

    #region ITenantDirectory

    /// <inheritdoc />
    async ValueTask<ReservationResult> ITenantDirectory.ReserveAsync(
        TenantReservation reservation,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );

        var tenantId = reservation.TenantId;
        var normalizedTenantId = Normalize(tenantId);
        var normalizedIssuer = Normalize(reservation.Issuer);

        var existingState = await dbContext
            .Tenants.AsNoTracking()
            .Where(entity => entity.NormalizedTenantId == normalizedTenantId)
            .Select(entity => entity.State)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingState == TenantRecord.StateRetired)
        {
            return ReservationResult.Conflict(
                $"The tenant identifier '{tenantId}' belonged to a deleted tenant and is never reissued."
            );
        }

        if (existingState is not null)
        {
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

        var formerHolder = await GetRetiredIssuerHolderCoreAsync(
            dbContext,
            normalizedIssuer,
            cancellationToken
        );
        if (
            formerHolder is not null
            && !string.Equals(formerHolder, tenantId, StringComparison.OrdinalIgnoreCase)
        )
        {
            // Relying parties that trusted a retired issuer would accept another tenant's tokens under it, so only the
            // tenant that gave it up can take it back.
            return ReservationResult.Conflict(
                $"The issuer '{reservation.Issuer}' belonged to another tenant and is never reissued."
            );
        }

        string? normalizedTeamId = null;
        if (reservation.TeamId is { } teamId)
        {
            normalizedTeamId = Normalize(teamId);
            var fullTeamDetail = await FindTeamAtLimitAsync(
                dbContext,
                normalizedTeamId,
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
    }

    /// <inheritdoc />
    async ValueTask ITenantDirectory.PlaceAsync(
        TenantPlacement placement,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );

        var normalizedTenantId = Normalize(placement.TenantId);
        var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
            entity => entity.NormalizedTenantId == normalizedTenantId,
            cancellationToken
        );

        var now = TimeProvider.GetUtcNow();
        if (tenant is null)
        {
            dbContext.Tenants.Add(
                new TenantRecord
                {
                    TenantId = placement.TenantId,
                    NormalizedTenantId = normalizedTenantId,
                    Issuer = placement.Issuer,
                    NormalizedIssuer = Normalize(placement.Issuer),
                    Cell = placement.Cell,
                    State = TenantRecord.StatePlaced,
                    CreatedWhen = now,
                    ModifiedWhen = now,
                }
            );
        }
        else if (tenant.State == TenantRecord.StateRetired)
        {
            // A retired identifier is never reissued, so a placement cannot bring it back.
            return;
        }
        else
        {
            tenant.State = TenantRecord.StatePlaced;
            tenant.Cell = placement.Cell;
            tenant.ModifiedWhen = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    async ValueTask ITenantDirectory.ReleaseAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTenantId = Normalize(tenantId);
        await dbContext
            .Tenants.Where(entity =>
                entity.NormalizedTenantId == normalizedTenantId
                && entity.State == TenantRecord.StateReserved
            )
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    async ValueTask ITenantDirectory.RetireAsync(
        TenantRetirement retirement,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
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

        var now = TimeProvider.GetUtcNow();
        if (tenant is null)
        {
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
        else if (tenant.State != TenantRecord.StateRetired)
        {
            // A retired record holds no issuer: the issuer is recorded as retired, below.
            tenant.State = TenantRecord.StateRetired;
            tenant.Issuer = null;
            tenant.NormalizedIssuer = null;
            tenant.NormalizedTeamId = null;
            tenant.ModifiedWhen = now;
        }

        if (retirement.Issuer is { } issuer)
        {
            await AddRetiredIssuerAsync(dbContext, tenantId, issuer, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    async ValueTask<ReservationResult> ITenantDirectory.ChangeIssuerAsync(
        TenantIssuerChange change,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );

        var tenantId = change.TenantId;
        var normalizedTenantId = Normalize(tenantId);

        if (change.Issuer is { } issuer)
        {
            var normalizedIssuer = Normalize(issuer);

            if (
                await dbContext.Tenants.AnyAsync(
                    entity =>
                        entity.NormalizedIssuer == normalizedIssuer
                        && entity.NormalizedTenantId != normalizedTenantId,
                    cancellationToken
                )
            )
            {
                return ReservationResult.Conflict(
                    $"Another tenant already has the issuer '{issuer}'."
                );
            }

            var formerHolder = await GetRetiredIssuerHolderCoreAsync(
                dbContext,
                normalizedIssuer,
                cancellationToken
            );
            if (
                formerHolder is not null
                && !string.Equals(formerHolder, tenantId, StringComparison.OrdinalIgnoreCase)
            )
            {
                // Relying parties that trusted a retired issuer would accept another tenant's tokens under it, so only
                // the tenant that gave it up can take it back.
                return ReservationResult.Conflict(
                    $"The issuer '{issuer}' belonged to another tenant and is never reissued."
                );
            }
        }

        if (
            change.PreviousIssuer is { } previousIssuer
            && !string.Equals(previousIssuer, change.Issuer, StringComparison.OrdinalIgnoreCase)
        )
        {
            await AddRetiredIssuerAsync(dbContext, tenantId, previousIssuer, cancellationToken);
        }

        var tenant = await dbContext.Tenants.SingleOrDefaultAsync(
            entity => entity.NormalizedTenantId == normalizedTenantId,
            cancellationToken
        );
        if (tenant is not null && tenant.State != TenantRecord.StateRetired)
        {
            tenant.Issuer = change.Issuer;
            tenant.NormalizedIssuer = change.Issuer is { } newIssuer ? Normalize(newIssuer) : null;
            tenant.ModifiedWhen = TimeProvider.GetUtcNow();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ReservationResult.Reserved();
    }

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
    /// Records that a tenant gave up an issuer, unless the issuer is already retired. Only the tenant that held an
    /// issuer can take it back, so an issuer that is already retired was retired by the same tenant or is refused.
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
    /// Finds a team, of the lineage of the specified team, that already holds the most tenants its limit allows. A
    /// team's limit is the lesser of the platform document's <c>max_tenants</c> setting and the team document's own, and
    /// it bounds the reserved and placed tenants of the team and of every team beneath it. The lineage is walked
    /// through the parent identifiers of the team documents, deepest team first.
    /// </summary>
    /// <param name="dbContext">The <see cref="DirectoryDbContext"/> to read from.</param>
    /// <param name="normalizedTeamId">The normalized identifier of the team that would hold the new tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The conflict detail when a team of the lineage is full; otherwise, <c>null</c>.</returns>
    // See ADR-1411.
    // See ADR-1426.
    private static async ValueTask<string?> FindTeamAtLimitAsync(
        DirectoryDbContext dbContext,
        string normalizedTeamId,
        CancellationToken cancellationToken
    )
    {
        var platformJson = await dbContext
            .Platforms.AsNoTracking()
            .Select(entity => entity.DocumentJson)
            .SingleOrDefaultAsync(cancellationToken);
        var platformLimit = ReadMaxTenants(
            platformJson is null
                ? null
                : JsonSerializer
                    .Deserialize<PlatformDocument>(platformJson, JsonOptions)
                    ?.SettingsJson
        );

        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? nextTeamId = normalizedTeamId;
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
                    entity.DocumentJson,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (team is null)
            {
                break;
            }

            nextTeamId = team.NormalizedParentTeamId;

            // A team's limit is the lesser of the platform's and its own; unset means unlimited.
            var ownLimit = ReadMaxTenants(
                team.DocumentJson is null
                    ? null
                    : JsonSerializer
                        .Deserialize<TeamDocument>(team.DocumentJson, JsonOptions)
                        ?.SettingsJson
            );
            var effectiveLimit = ownLimit is { } own
                ? platformLimit is { } platform
                    ? Math.Min(platform, own)
                    : own
                : platformLimit;
            if (effectiveLimit is not { } maxTenants)
            {
                continue;
            }

            var subtree = await GetSubtreeAsync(dbContext, currentTeamId, cancellationToken);
            var count = await dbContext
                .Tenants.Where(entity =>
                    (
                        entity.State == TenantRecord.StateReserved
                        || entity.State == TenantRecord.StatePlaced
                    ) && subtree.Contains(entity.NormalizedTeamId)
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
    /// Gets the normalized identifiers of a team and of every team beneath it, found through the parent identifiers of
    /// the team documents.
    /// </summary>
    private static async ValueTask<List<string?>> GetSubtreeAsync(
        DirectoryDbContext dbContext,
        string normalizedTeamId,
        CancellationToken cancellationToken
    )
    {
        List<string?> subtree = [normalizedTeamId];
        var visited = new HashSet<string>(StringComparer.Ordinal) { normalizedTeamId };
        List<string?> frontier = [normalizedTeamId];

        while (frontier.Count > 0)
        {
            var parents = frontier;
            var children = await dbContext
                .Teams.AsNoTracking()
                .Where(entity => parents.Contains(entity.NormalizedParentTeamId))
                .Select(entity => entity.NormalizedTeamId)
                .ToListAsync(cancellationToken);

            frontier = [.. children.Where(visited.Add)];
            subtree.AddRange(frontier);
        }

        return subtree;
    }

    private static int? ReadMaxTenants(string? settingsJson)
    {
        if (string.IsNullOrEmpty(settingsJson))
        {
            return null;
        }

        Dictionary<string, JsonElement>? settings;
        try
        {
            settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                settingsJson,
                JsonOptions
            );
        }
        catch (JsonException)
        {
            return null;
        }

        return
            settings is not null
            && settings.TryGetValue(MaxTenantsSettingName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var maxTenants)
            ? maxTenants
            : null;
    }

    #endregion

    #region ITeamDirectory

    /// <inheritdoc />
    async ValueTask<ReservationResult> ITeamDirectory.ReserveAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
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
                State = TeamRecord.StateReserved,
            }
        );
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ReservationResult.Reserved();
    }

    /// <inheritdoc />
    async ValueTask ITeamDirectory.PublishAsync(
        TeamDocument document,
        CancellationToken cancellationToken
    )
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );

        var normalizedTeamId = Normalize(document.TeamId);
        var team = await dbContext.Teams.SingleOrDefaultAsync(
            entity => entity.NormalizedTeamId == normalizedTeamId,
            cancellationToken
        );
        if (team is { State: TeamRecord.StateRetired })
        {
            // A retired identifier is never reissued, so a document cannot bring it back.
            return;
        }

        // The directory assigns the sequence, so the stored document carries none: it is read back from the record.
        var documentJson = JsonSerializer.Serialize(document with { Sequence = 0 }, JsonOptions);
        var change = new DirectoryChangeRecord
        {
            TeamId = document.TeamId,
            DocumentJson = documentJson,
        };
        dbContext.Changes.Add(change);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (team is null)
        {
            team = new TeamRecord
            {
                TeamId = document.TeamId,
                NormalizedTeamId = normalizedTeamId,
                State = TeamRecord.StatePublished,
            };
            dbContext.Teams.Add(team);
        }

        team.State = TeamRecord.StatePublished;
        team.NormalizedParentTeamId = document.ParentTeamId is { } parentTeamId
            ? Normalize(parentTeamId)
            : null;
        team.DocumentJson = documentJson;
        team.Sequence = change.Sequence;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    async ValueTask ITeamDirectory.ReleaseAsync(string teamId, CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTeamId = Normalize(teamId);
        await dbContext
            .Teams.Where(entity =>
                entity.NormalizedTeamId == normalizedTeamId
                && entity.State == TeamRecord.StateReserved
            )
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    async ValueTask ITeamDirectory.RetireAsync(string teamId, CancellationToken cancellationToken)
    {
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );

        var normalizedTeamId = Normalize(teamId);
        var team = await dbContext.Teams.SingleOrDefaultAsync(
            entity => entity.NormalizedTeamId == normalizedTeamId,
            cancellationToken
        );
        if (team is { State: TeamRecord.StateRetired })
        {
            return;
        }

        var change = new DirectoryChangeRecord { TeamId = teamId, DocumentJson = null };
        dbContext.Changes.Add(change);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (team is null)
        {
            team = new TeamRecord
            {
                TeamId = teamId,
                NormalizedTeamId = normalizedTeamId,
                State = TeamRecord.StateRetired,
            };
            dbContext.Teams.Add(team);
        }

        team.State = TeamRecord.StateRetired;
        team.DocumentJson = null;
        team.Sequence = change.Sequence;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

        return JsonSerializer.Deserialize<TeamDocument>(documentJson, JsonOptions) is { } document
            ? document with
            {
                Sequence = team.Sequence,
            }
            : null;
    }

    /// <inheritdoc />
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
                : JsonSerializer.Deserialize<TeamDocument>(record.DocumentJson, JsonOptions);
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
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );

        // The directory assigns the sequence, so the stored document carries none: it is read back from the record.
        var documentJson = JsonSerializer.Serialize(document with { Sequence = 0 }, JsonOptions);
        var change = new DirectoryChangeRecord { TeamId = null, DocumentJson = documentJson };
        dbContext.Changes.Add(change);
        await dbContext.SaveChangesAsync(cancellationToken);

        var platform = await dbContext.Platforms.SingleOrDefaultAsync(
            entity => entity.Id == PlatformRecord.SingletonId,
            cancellationToken
        );
        if (platform is null)
        {
            dbContext.Platforms.Add(
                new PlatformRecord { DocumentJson = documentJson, Sequence = change.Sequence }
            );
        }
        else
        {
            platform.DocumentJson = documentJson;
            platform.Sequence = change.Sequence;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
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

    private static PlatformDocument? ReadPlatformDocument(string? documentJson, long sequence)
    {
        if (documentJson is null)
        {
            return null;
        }

        return
            JsonSerializer.Deserialize<PlatformDocument>(documentJson, JsonOptions) is { } document
            ? document with
            {
                Sequence = sequence,
            }
            : null;
    }

    #endregion
}
