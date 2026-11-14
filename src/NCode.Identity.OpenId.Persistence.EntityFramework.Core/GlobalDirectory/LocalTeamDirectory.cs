// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using IdGen;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ITeamDirectory"/> of a cell that runs on its own. It answers from the cell's own team tables:
/// the team rows and the retired team identifiers. The team row is the published document, so publishing writes
/// nothing and the change feed is always empty. Retiring a team adds its retired row to the unit of work of the
/// <see cref="OpenIdDbContext"/>, which commits it with the caller's save.
/// </summary>
// See ADR-1411.
internal sealed class LocalTeamDirectory(
    OpenIdDbContext dbContext,
    IIdGenerator<long> idGenerator,
    IAmbientTenantAccessor ambientTenantAccessor
) : ITeamDirectory
{
    private const string UnscopedReason = "directory";

    private OpenIdDbContext DbContext { get; } = dbContext;

    private IIdGenerator<long> IdGenerator { get; } = idGenerator;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    private static string Normalize(string value) => value.ToLowerInvariant();

    /// <inheritdoc />
    public async ValueTask<ReservationResult> ReserveAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var normalizedTeamId = Normalize(teamId);

        if (
            await DbContext.RetiredTeams.AnyAsync(
                entity => entity.NormalizedTeamId == normalizedTeamId,
                cancellationToken
            )
        )
        {
            return ReservationResult.Conflict(
                $"The team identifier '{teamId}' belonged to a deleted team and is never reissued."
            );
        }

        if (
            await DbContext.Teams.AnyAsync(
                entity => entity.NormalizedTeamId == normalizedTeamId,
                cancellationToken
            )
        )
        {
            return ReservationResult.Conflict(
                $"Another team already has the identifier '{teamId}'."
            );
        }

        return ReservationResult.Reserved();
    }

    /// <inheritdoc />
    public ValueTask PublishAsync(TeamDocument document, CancellationToken cancellationToken)
    {
        // The team row is the published document.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask RetireAsync(string teamId, CancellationToken cancellationToken)
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var normalizedTeamId = Normalize(teamId);
        var isRetired =
            DbContext.RetiredTeams.Local.Any(entity => entity.NormalizedTeamId == normalizedTeamId)
            || await DbContext.RetiredTeams.AnyAsync(
                entity => entity.NormalizedTeamId == normalizedTeamId,
                cancellationToken
            );
        if (isRetired)
        {
            return;
        }

        var heldTeamId =
            await DbContext
                .Teams.AsNoTracking()
                .Where(entity => entity.NormalizedTeamId == normalizedTeamId)
                .Select(entity => entity.TeamId)
                .SingleOrDefaultAsync(cancellationToken)
            ?? teamId;

        await DbContext.RetiredTeams.AddAsync(
            new RetiredTeamEntity
            {
                Id = IdGenerator.CreateId(),
                TeamId = heldTeamId,
                NormalizedTeamId = normalizedTeamId,
            },
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsTeamIdRetiredAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var normalizedTeamId = Normalize(teamId);
        return await DbContext.RetiredTeams.AnyAsync(
            entity => entity.NormalizedTeamId == normalizedTeamId,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask<TeamDocument?> GetOrDefaultAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var normalizedTeamId = Normalize(teamId);
        var team = await DbContext
            .Teams.AsNoTracking()
            .Where(entity => entity.NormalizedTeamId == normalizedTeamId)
            .Select(entity => new
            {
                entity.TeamId,
                entity.DisplayName,
                entity.SettingsJson,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (team is null)
        {
            return null;
        }

        return new TeamDocument
        {
            TeamId = team.TeamId,
            DisplayName = team.DisplayName,
            SettingsJson = team.SettingsJson.GetRawText(),
        };
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DirectoryChange>> ListChangesAsync(
        long after,
        int limit,
        CancellationToken cancellationToken
    )
    {
        // A cell that runs on its own has no other cell to follow.
        return ValueTask.FromResult<IReadOnlyList<DirectoryChange>>([]);
    }
}
