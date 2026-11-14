// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using IdGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ITenantDirectory"/> of a cell that runs on its own. It answers from the cell's own tables:
/// the retired tenant identifiers and issuers, the tenant rows and the tenant count of each team. The tenant row is the
/// placement, so placing and releasing a tenant write nothing, and retiring a tenant adds its retired rows to the unit
/// of work of the <see cref="OpenIdDbContext"/>, which commits them with the caller's save.
/// </summary>
// See ADR-1160.
// See ADR-1411.
internal sealed class LocalTenantDirectory(
    OpenIdDbContext dbContext,
    IIdGenerator<long> idGenerator,
    IAmbientTenantAccessor ambientTenantAccessor,
    IOptions<CellOptions> cellOptions
) : ITenantDirectory
{
    private const string UnscopedReason = "directory";

    // The name of the 'max_tenants' team setting (OpenIdSettingNames.MaxTenants). The persistence slices do not
    // reference the OpenID abstractions that declare it.
    private const string MaxTenantsSettingName = "max_tenants";

    private OpenIdDbContext DbContext { get; } = dbContext;

    private IIdGenerator<long> IdGenerator { get; } = idGenerator;

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

        var tenantId = reservation.TenantId;
        var normalizedTenantId = Normalize(tenantId);
        var normalizedIssuer = Normalize(reservation.Issuer);

        if (
            await DbContext.RetiredTenants.AnyAsync(
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
            await DbContext.Tenants.AnyAsync(
                entity => entity.NormalizedTenantId == normalizedTenantId,
                cancellationToken
            )
        )
        {
            return ReservationResult.Conflict(
                $"Another tenant already has the identifier '{tenantId}'."
            );
        }

        if (
            await DbContext.Tenants.AnyAsync(
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

        if (reservation.TeamId is { } teamId)
        {
            var fullTeamDetail = await FindTeamAtLimitAsync(teamId, cancellationToken);
            if (fullTeamDetail is not null)
            {
                return ReservationResult.Conflict(fullTeamDetail);
            }
        }

        return ReservationResult.Reserved();
    }

    /// <inheritdoc />
    public ValueTask PlaceAsync(TenantPlacement placement, CancellationToken cancellationToken)
    {
        // The tenant row is the placement.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ReleaseAsync(string tenantId, CancellationToken cancellationToken)
    {
        // A reservation writes nothing, so there is nothing to release.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask RetireAsync(string tenantId, CancellationToken cancellationToken)
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var normalizedTenantId = Normalize(tenantId);
        var tenant = await DbContext
            .Tenants.AsNoTracking()
            .Where(entity => entity.NormalizedTenantId == normalizedTenantId)
            .Select(entity => new
            {
                entity.TenantId,
                entity.Issuer,
                entity.NormalizedIssuer,
            })
            .SingleOrDefaultAsync(cancellationToken);

        var heldTenantId = tenant?.TenantId ?? tenantId;

        var isTenantIdRetired =
            DbContext.RetiredTenants.Local.Any(entity =>
                entity.NormalizedTenantId == normalizedTenantId
            )
            || await DbContext.RetiredTenants.AnyAsync(
                entity => entity.NormalizedTenantId == normalizedTenantId,
                cancellationToken
            );
        if (!isTenantIdRetired)
        {
            await DbContext.RetiredTenants.AddAsync(
                new RetiredTenantEntity
                {
                    Id = IdGenerator.CreateId(),
                    TenantId = heldTenantId,
                    NormalizedTenantId = normalizedTenantId,
                },
                cancellationToken
            );
        }

        if (tenant is not { Issuer: { } issuer, NormalizedIssuer: { } normalizedIssuer })
        {
            return;
        }

        var isIssuerRetired =
            DbContext.RetiredIssuers.Local.Any(entity =>
                entity.NormalizedIssuer == normalizedIssuer
            )
            || await DbContext.RetiredIssuers.AnyAsync(
                entity => entity.NormalizedIssuer == normalizedIssuer,
                cancellationToken
            );
        if (!isIssuerRetired)
        {
            await DbContext.RetiredIssuers.AddAsync(
                new RetiredIssuerEntity
                {
                    Id = IdGenerator.CreateId(),
                    Issuer = issuer,
                    NormalizedIssuer = normalizedIssuer,
                    TenantId = heldTenantId,
                },
                cancellationToken
            );
        }
    }

    /// <inheritdoc />
    public async ValueTask<TenantPlacement?> GetPlacementOrDefaultAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var normalizedTenantId = Normalize(tenantId);
        var tenant = await DbContext
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

        var normalizedTenantId = Normalize(tenantId);
        return await DbContext.RetiredTenants.AnyAsync(
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

        return await GetRetiredIssuerHolderCoreAsync(Normalize(issuer), cancellationToken);
    }

    private async ValueTask<string?> GetRetiredIssuerHolderCoreAsync(
        string normalizedIssuer,
        CancellationToken cancellationToken
    )
    {
        return await DbContext
            .RetiredIssuers.Where(entity => entity.NormalizedIssuer == normalizedIssuer)
            .Select(entity => entity.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Finds a team, of the lineage of the specified team, that already holds the most tenants its own
    /// <c>max_tenants</c> setting allows. A team's limit bounds the tenants of the team and of every team beneath it.
    /// </summary>
    /// <param name="teamId">The identifier of the team that would hold the new tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The conflict detail when a team of the lineage is full; otherwise, <c>null</c>.</returns>
    // See ADR-1411.
    // See ADR-1426.
    private async ValueTask<string?> FindTeamAtLimitAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        var normalizedTeamId = Normalize(teamId);
        var path = await DbContext
            .Teams.AsNoTracking()
            .Where(entity => entity.NormalizedTeamId == normalizedTeamId)
            .Select(entity => entity.Path)
            .SingleOrDefaultAsync(cancellationToken);
        if (path is null)
        {
            return null;
        }

        List<string> normalizedLineageIds = [.. TeamPaths.Parse(path).Select(Normalize)];
        var lineage = await DbContext
            .Teams.AsNoTracking()
            .Where(entity => normalizedLineageIds.Contains(entity.NormalizedTeamId))
            .Select(entity => new
            {
                entity.TeamId,
                entity.Path,
                entity.SettingsJson,
            })
            .ToListAsync(cancellationToken);

        foreach (var team in lineage)
        {
            if (
                team.SettingsJson.ValueKind != JsonValueKind.Object
                || !team.SettingsJson.TryGetProperty(MaxTenantsSettingName, out var limitElement)
                || !limitElement.TryGetInt32(out var maxTenants)
            )
            {
                continue;
            }

            var teamPath = team.Path;
            var count = await DbContext
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
