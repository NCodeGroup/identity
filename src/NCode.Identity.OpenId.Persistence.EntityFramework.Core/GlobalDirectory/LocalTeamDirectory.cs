// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
using IdGen;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ITeamDirectory"/> of a cell that runs on its own. It answers from the cell's own team tables:
/// the team rows and the retired team identifiers. The team row is the published document, so publishing writes
/// nothing and the change feed is always empty. Every call is its own atomic operation: it opens an
/// <see cref="OpenIdDbContext"/> of its own and commits its writes before it returns, so retiring a team is durable
/// whether or not the caller saves anything else.
/// </summary>
// See ADR-1411.
internal sealed class LocalTeamDirectory(
    IDbContextFactory<OpenIdDbContext> dbContextFactory,
    IIdGenerator<long> idGenerator,
    IAmbientTenantAccessor ambientTenantAccessor
) : ITeamDirectory
{
    private const string UnscopedReason = "directory";

    private IDbContextFactory<OpenIdDbContext> DbContextFactory { get; } = dbContextFactory;

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
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTeamId = Normalize(teamId);

        if (
            await dbContext.RetiredTeams.AnyAsync(
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
            await dbContext.Teams.AnyAsync(
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
    public ValueTask ReleaseAsync(string teamId, CancellationToken cancellationToken)
    {
        // A reservation writes no row, because the team row is the reservation, so there is nothing to release.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask RetireAsync(string teamId, CancellationToken cancellationToken)
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTeamId = Normalize(teamId);
        if (
            await dbContext.RetiredTeams.AnyAsync(
                entity => entity.NormalizedTeamId == normalizedTeamId,
                cancellationToken
            )
        )
        {
            return;
        }

        var heldTeamId =
            await dbContext
                .Teams.AsNoTracking()
                .Where(entity => entity.NormalizedTeamId == normalizedTeamId)
                .Select(entity => entity.TeamId)
                .SingleOrDefaultAsync(cancellationToken)
            ?? teamId;

        dbContext.RetiredTeams.Add(
            new RetiredTeamEntity
            {
                Id = IdGenerator.CreateId(),
                TeamId = heldTeamId,
                NormalizedTeamId = normalizedTeamId,
            }
        );
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsTeamIdRetiredAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTeamId = Normalize(teamId);
        return await dbContext.RetiredTeams.AnyAsync(
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
        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var normalizedTeamId = Normalize(teamId);
        var team = await dbContext
            .Teams.AsNoTracking()
            .Where(entity => entity.NormalizedTeamId == normalizedTeamId)
            .Select(entity => new
            {
                entity.TeamId,
                entity.DisplayName,
                entity.SettingsJson,
                entity.RootKeysJson,
                ParentTeamId = entity.ParentTeam == null ? null : entity.ParentTeam.TeamId,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (team is null)
        {
            return null;
        }

        var rootKeys =
            JsonSerializer.Deserialize<Dictionary<string, OwnerRootKeyRequest>>(team.RootKeysJson)
            ?? [];

        return new TeamDocument
        {
            TeamId = team.TeamId,
            DisplayName = team.DisplayName,
            ParentTeamId = team.ParentTeamId,
            SettingsJson = team.SettingsJson.GetRawText(),
            RootKeys = rootKeys.ToImmutableDictionary(
                pair => pair.Key,
                pair => new TeamRootKeyReference(pair.Value.KeyWrapperName, pair.Value.KeyReference)
                {
                    AccessIdentity = pair.Value.AccessIdentity,
                },
                StringComparer.Ordinal
            ),
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
