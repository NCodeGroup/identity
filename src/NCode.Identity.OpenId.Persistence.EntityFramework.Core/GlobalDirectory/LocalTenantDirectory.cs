// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ITenantDirectory"/> of a cell that runs on its own. It answers from the cell's own tables:
/// the retired tenant identifiers and issuers, the tenant rows and the tenant count of each team. The tenant row is the
/// placement, so placing and releasing a tenant write nothing. Every call is its own atomic operation: it opens an
/// <see cref="OpenIdDbContext"/> of its own and commits its writes before it returns, so retiring a tenant is durable
/// whether or not the caller saves anything else.
/// </summary>
// See ADR-1160.
// See ADR-1411.
internal sealed class LocalTenantDirectory(
    IDbContextFactory<OpenIdDbContext> dbContextFactory,
    ISurrogateKeyGenerator keyGenerator,
    IAmbientTenantAccessor ambientTenantAccessor,
    IOptions<CellOptions> cellOptions
) : ITenantDirectory
{
    private const string UnscopedReason = "directory";

    private IDbContextFactory<OpenIdDbContext> DbContextFactory { get; } = dbContextFactory;

    private ISurrogateKeyGenerator KeyGenerator { get; } = keyGenerator;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    private static string Normalize(string value) => value.ToLowerInvariant();

    /// <inheritdoc />
    public async ValueTask<ReservationResult> ReserveAsync(
        TenantReservation reservation,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var tenantId = reservation.TenantId;
        var normalizedTenantId = Normalize(tenantId);

        if (
            await dbContext.RetiredTenants.AnyAsync(
                entity => entity.NormalizedTenantId == normalizedTenantId,
                cancellationToken
            )
        )
        {
            return ReservationResult.Conflict(
                $"The tenant identifier '{tenantId}' belonged to a deleted tenant and is never reissued."
            );
        }

        if (
            await dbContext.Tenants.AnyAsync(
                entity => entity.NormalizedTenantId == normalizedTenantId,
                cancellationToken
            )
        )
        {
            return ReservationResult.Conflict(
                $"Another tenant already has the identifier '{tenantId}'."
            );
        }

        // A tenant without an issuer is reserved by its identifier alone: no issuer is judged, because a null would
        // match every tenant without one and every retired record.
        if (reservation.Issuer is { } issuer)
        {
            var normalizedIssuer = Normalize(issuer);
            if (
                await dbContext.Tenants.AnyAsync(
                    entity => entity.NormalizedIssuer == normalizedIssuer,
                    cancellationToken
                )
            )
            {
                return ReservationResult.Conflict(
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
                return ReservationResult.Conflict(retiredIssuerDetail);
            }
        }

        if (reservation.TeamId is { } teamId)
        {
            var fullTeamDetail = await FindTeamAtLimitAsync(dbContext, teamId, cancellationToken);
            if (fullTeamDetail is not null)
            {
                return ReservationResult.Conflict(fullTeamDetail);
            }
        }

        return ReservationResult.Reserved();
    }

    /// <inheritdoc />
    public ValueTask<ReservationResult> PlaceAsync(
        TenantPlacementRequest placement,
        CancellationToken cancellationToken
    )
    {
        // The tenant row is the placement.
        var reserved = ReservationResult.Reserved();
        return ValueTask.FromResult(reserved);
    }

    /// <inheritdoc />
    public ValueTask ReleaseAsync(string tenantId, CancellationToken cancellationToken)
    {
        // A reservation writes nothing, so there is nothing to release.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<MoveOfferResult> OfferMoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    )
    {
        // A cell that runs on its own has no other cell to offer a tenant to.
        var conflict = MoveOfferResult.Conflict(
            $"The cell '{CellOptions.Name}' runs on its own, so it has no other cell to offer the tenant '{tenantId}' to."
        );
        return ValueTask.FromResult(conflict);
    }

    /// <inheritdoc />
    public ValueTask<MoveResult> MoveAsync(
        string tenantId,
        string fromCell,
        string toCell,
        CancellationToken cancellationToken
    )
    {
        // A cell that runs on its own has no other cell to move a tenant to.
        var conflict = MoveResult.Conflict(
            $"The cell '{CellOptions.Name}' runs on its own, so it has no other cell to move the tenant '{tenantId}' to."
        );
        return ValueTask.FromResult(conflict);
    }

    /// <inheritdoc />
    public async ValueTask RetireAsync(
        TenantRetirement retirement,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var tenantId = retirement.TenantId;
        var normalizedTenantId = Normalize(tenantId);

        if (
            !await dbContext.RetiredTenants.AnyAsync(
                entity => entity.NormalizedTenantId == normalizedTenantId,
                cancellationToken
            )
        )
        {
            var retiredTenantEntity = new RetiredTenantEntity
            {
                Id = KeyGenerator.CreateKey(),
                TenantId = tenantId,
                NormalizedTenantId = normalizedTenantId,
            };
            dbContext.RetiredTenants.Add(retiredTenantEntity);
        }

        if (retirement.Issuer is { } issuer)
        {
            await AddRetiredIssuerAsync(dbContext, tenantId, issuer, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<ReservationResult> ChangeIssuerAsync(
        TenantIssuerChange change,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

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

            var retiredIssuerDetail = await FindRetiredIssuerConflictAsync(
                dbContext,
                tenantId,
                issuer,
                cancellationToken
            );
            if (retiredIssuerDetail is not null)
            {
                return ReservationResult.Conflict(retiredIssuerDetail);
            }
        }

        if (
            change.PreviousIssuer is { } previousIssuer
            && !string.Equals(previousIssuer, change.Issuer, StringComparison.OrdinalIgnoreCase)
        )
        {
            await AddRetiredIssuerAsync(dbContext, tenantId, previousIssuer, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ReservationResult.Reserved();
    }

    /// <summary>
    /// Records that a tenant gave up an issuer, unless it is already retired, so the first tenant to retire an issuer
    /// stays its holder.
    /// </summary>
    /// <param name="dbContext">The <see cref="OpenIdDbContext"/> to write to.</param>
    /// <param name="tenantId">The identifier of the tenant that gave up the issuer.</param>
    /// <param name="issuer">The issuer that the tenant gave up.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    private async ValueTask AddRetiredIssuerAsync(
        OpenIdDbContext dbContext,
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

        var retiredIssuerEntity = new RetiredIssuerEntity
        {
            Id = KeyGenerator.CreateKey(),
            Issuer = issuer,
            NormalizedIssuer = normalizedIssuer,
            TenantId = tenantId,
        };
        dbContext.RetiredIssuers.Add(retiredIssuerEntity);
    }

    /// <inheritdoc />
    public async ValueTask<TenantPlacement?> GetPlacementOrDefaultAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTenantId = Normalize(tenantId);
        var tenant = await dbContext
            .Tenants.AsNoTracking()
            .Where(entity => entity.NormalizedTenantId == normalizedTenantId)
            .Select(entity => new { entity.TenantId, entity.Issuer })
            .SingleOrDefaultAsync(cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        return new TenantPlacement
        {
            TenantId = tenant.TenantId,
            Issuer = tenant.Issuer ?? string.Empty,
            Cell = CellOptions.Name,
            Region = CellOptions.Region,
        };
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsTenantIdRetiredAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTenantId = Normalize(tenantId);
        return await dbContext.RetiredTenants.AnyAsync(
            entity => entity.NormalizedTenantId == normalizedTenantId,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetRetiredIssuerHolderOrDefaultAsync(
        string issuer,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedIssuer = Normalize(issuer);
        return await GetRetiredIssuerHolderCoreAsync(
            dbContext,
            normalizedIssuer,
            cancellationToken
        );
    }

    /// <summary>
    /// Finds whether an issuer is retired by a tenant other than the specified one. Relying parties that trusted a
    /// retired issuer would accept another tenant's tokens under it, so only the tenant that gave it up can take it
    /// back.
    /// </summary>
    /// <param name="dbContext">The <see cref="OpenIdDbContext"/> to read from.</param>
    /// <param name="tenantId">The identifier of the tenant that would hold the issuer.</param>
    /// <param name="issuer">The issuer that the tenant would hold.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The conflict detail when another tenant retired the issuer; otherwise, <c>null</c>.</returns>
    private static async ValueTask<string?> FindRetiredIssuerConflictAsync(
        OpenIdDbContext dbContext,
        string tenantId,
        string issuer,
        CancellationToken cancellationToken
    )
    {
        var normalizedIssuer = Normalize(issuer);
        var formerHolder = await GetRetiredIssuerHolderCoreAsync(
            dbContext,
            normalizedIssuer,
            cancellationToken
        );
        return
            formerHolder is not null
            && !string.Equals(formerHolder, tenantId, StringComparison.OrdinalIgnoreCase)
            ? $"The issuer '{issuer}' belonged to another tenant and is never reissued."
            : null;
    }

    private static async ValueTask<string?> GetRetiredIssuerHolderCoreAsync(
        OpenIdDbContext dbContext,
        string normalizedIssuer,
        CancellationToken cancellationToken
    )
    {
        return await dbContext
            .RetiredIssuers.Where(entity => entity.NormalizedIssuer == normalizedIssuer)
            .Select(entity => entity.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Finds a team of the lineage of the specified team that already holds the most tenants its limit allows. A team's
    /// limit is the lesser of the platform's <c>max_tenants</c> setting and the team's own, and it bounds the tenants
    /// of the team and of every team beneath it. The deepest team is checked first.
    /// </summary>
    /// <param name="dbContext">The <see cref="OpenIdDbContext"/> to read from.</param>
    /// <param name="teamId">The identifier of the team that would hold the new tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The conflict detail when the team has no record, a team of the lineage is full or a <c>max_tenants</c> setting
    /// cannot be read; otherwise, <c>null</c>.
    /// </returns>
    // See ADR-1411.
    // See ADR-1426.
    private static async ValueTask<string?> FindTeamAtLimitAsync(
        OpenIdDbContext dbContext,
        string teamId,
        CancellationToken cancellationToken
    )
    {
        var normalizedTeamId = Normalize(teamId);
        var path = await dbContext
            .Teams.AsNoTracking()
            .Where(entity => entity.NormalizedTeamId == normalizedTeamId)
            .Select(entity => entity.Path)
            .SingleOrDefaultAsync(cancellationToken);
        if (path is null)
        {
            return $"The team '{teamId}' has no record.";
        }

        List<string> normalizedLineageIds = [.. TeamPaths.Parse(path).Select(Normalize)];

        var platformSettings = await dbContext
            .Platforms.AsNoTracking()
            .Select(entity => new { entity.SettingsJson })
            .SingleOrDefaultAsync(cancellationToken);
        int? platformLimit = null;
        if (
            platformSettings is not null
            && !TenantLimitRules.TryReadMaxTenants(platformSettings.SettingsJson, out platformLimit)
        )
        {
            return "The platform has a max_tenants setting the directory cannot read.";
        }

        var lineage = await dbContext
            .Teams.AsNoTracking()
            .Where(entity => normalizedLineageIds.Contains(entity.NormalizedTeamId))
            .Select(entity => new
            {
                entity.TeamId,
                entity.Path,
                entity.SettingsJson,
            })
            .ToListAsync(cancellationToken);

        foreach (var team in lineage.OrderByDescending(entity => entity.Path.Length))
        {
            if (!TenantLimitRules.TryReadMaxTenants(team.SettingsJson, out var ownLimit))
            {
                return $"The team '{team.TeamId}' has a max_tenants setting the directory cannot read.";
            }

            var effectiveLimit = TenantLimitRules.GetEffectiveLimit(platformLimit, ownLimit);
            if (effectiveLimit is not { } maxTenants)
            {
                continue;
            }

            var teamPath = team.Path;
            var count = await dbContext
                .Tenants.Where(tenant => tenant.Team.Path.StartsWith(teamPath))
                .LongCountAsync(cancellationToken);
            if (count >= maxTenants)
            {
                return $"The team '{team.TeamId}' holds its maximum of {maxTenants} tenants.";
            }
        }

        return null;
    }
}
