// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Teams;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Mediator;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="ITeamDocumentApplier"/>. It mirrors a published team document
/// into the team's row, or removes the row of a retired team, then sends a <see cref="TeamChangedCommand"/> so caches
/// evict the team. A team that tenants or teams still name is disabled instead of removed, because the cell refuses to
/// delete a team that something names. The root key that the document holds for the cell's region is mirrored into the
/// team's owner root key, which rewraps the master keys of the team's tenants in this cell. Reconciling brings the owner
/// root key of every enabled mirrored team to the reference its row holds for the region, which applies a key that
/// failed its probe or its rotation cooldown once it can be.
/// </summary>
// See ADR-1411 and ADR-1416.
internal sealed class DefaultTeamDocumentApplier(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    IServiceScopeFactory serviceScopeFactory,
    IOwnerRootKeyManager ownerRootKeyManager,
    IOptions<CellOptions> cellOptionsAccessor,
    ILogger<DefaultTeamDocumentApplier> logger
) : ITeamDocumentApplier
{
    // The teams are read in pages, so the reconcile never holds every team at once.
    private const int TeamPageSize = 100;

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;
    private IOwnerRootKeyManager OwnerRootKeyManager { get; } = ownerRootKeyManager;
    private CellOptions CellOptions { get; } = cellOptionsAccessor.Value;
    private ILogger<DefaultTeamDocumentApplier> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask ApplyAsync(TeamDocument document, CancellationToken cancellationToken)
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped("directory team mirror");

        var settings = JsonElement.Parse(document.SettingsJson);
        var persistedTeam = new PersistedTeam
        {
            TeamId = document.TeamId,
            ParentTeamId = document.ParentTeamId,
            IsDisabled = false,
            DisplayName = document.DisplayName,
            DirectorySequence = document.Sequence,
            RootKeys = document.RootKeys.ToDictionary(
                pair => pair.Key,
                pair => new OwnerRootKeyRequest(pair.Value.KeyWrapperName, pair.Value.KeyReference)
                {
                    AccessIdentity = pair.Value.AccessIdentity,
                },
                StringComparer.Ordinal
            ),
            Settings = new PersistedTeamSettings
            {
                TeamId = document.TeamId,
                ConcurrencyToken = string.Empty,
                Value = settings,
            },
        };

        bool isApplied;
        bool settingsChanged;
        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            var store = storeManager.GetStore<ITeamStore>();

            var storedSettings = await store.GetSettingsOrDefaultAsync(
                document.TeamId,
                cancellationToken
            );
            settingsChanged =
                storedSettings is null || !JsonElement.DeepEquals(storedSettings.Value, settings);

            isApplied = await store.MirrorAsync(persistedTeam, cancellationToken);
            if (isApplied)
            {
                await storeManager.SaveChangesAsync(cancellationToken);
            }
        }

        if (isApplied)
        {
            await SendAsync(
                new TeamChangedCommand(document.TeamId, settingsChanged),
                cancellationToken
            );

            await MirrorRootKeyAsync(
                document.TeamId,
                persistedTeam.RootKeys.GetValueOrDefault(CellOptions.Region),
                cancellationToken
            );
        }
    }

    /// <inheritdoc />
    public async ValueTask ReconcileRootKeysAsync(CancellationToken cancellationToken)
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped("directory team mirror");

        var region = CellOptions.Region;
        var page = new PageRequest { Limit = TeamPageSize };
        do
        {
            var teams = await GetTeamPageAsync(page, cancellationToken);
            foreach (var team in teams.Items)
            {
                if (team.IsDisabled)
                    continue;

                await MirrorRootKeyAsync(
                    team.TeamId,
                    team.RootKeys.GetValueOrDefault(region),
                    cancellationToken
                );
            }

            page = page with { Cursor = teams.NextCursor };
        } while (page.Cursor is not null);
    }

    private async ValueTask<PagedResult<PersistedTeam>> GetTeamPageAsync(
        PageRequest page,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ITeamStore>();
        return await store.GetPageAsync(page, cancellationToken);
    }

    // A key that fails validation is logged and left unapplied, so one team's key never stops another's; the team row
    // and its command are already applied, and the next document the directory publishes for the team, or the next
    // reconcile, retries the key.
    private async ValueTask MirrorRootKeyAsync(
        string teamId,
        OwnerRootKeyRequest? reference,
        CancellationToken cancellationToken
    )
    {
        var region = CellOptions.Region;
        var owner = new SecretOwner(SecretOwnerTypes.Team, teamId).Normalize();
        try
        {
            var current = await OwnerRootKeyManager.GetAsync(owner, cancellationToken);
            if (reference is null)
            {
                if (current is not null)
                    await OwnerRootKeyManager.RemoveAsync(owner, cancellationToken);

                return;
            }

            if (
                current is not null
                && string.Equals(
                    current.KeyWrapperName,
                    reference.KeyWrapperName,
                    StringComparison.Ordinal
                )
                && string.Equals(
                    current.KeyReference,
                    reference.KeyReference,
                    StringComparison.Ordinal
                )
                && string.Equals(
                    current.AccessIdentity,
                    reference.AccessIdentity,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }

            await OwnerRootKeyManager.SetAsync(owner, reference, cancellationToken);
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or OwnerRootKeyNotAllowedException
                        or OwnerRootKeyProbeException
                        or MasterKeyRotationTooSoonException
                        or StoreConcurrencyException
            )
        {
            Logger.DirectoryTeamRootKeyNotApplied(teamId, region, exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask RetireAsync(
        string teamId,
        long sequence,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped("directory team mirror");

        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            var store = storeManager.GetStore<ITeamStore>();

            var persistedTeam = await store.GetOrDefaultAsync(teamId, cancellationToken);
            if (persistedTeam is null)
                return;

            if (await store.HasDependentsAsync(teamId, cancellationToken))
            {
                persistedTeam.IsDisabled = true;
                await store.UpdateAsync(persistedTeam, cancellationToken);
                Logger.DirectoryRetiredTeamHasDependents(teamId, sequence);
            }
            else
            {
                await store.RemoveAsync(teamId, cancellationToken);
            }

            await storeManager.SaveChangesAsync(cancellationToken);
        }

        await SendAsync(new TeamChangedCommand(teamId, false), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<long> GetAppliedSequenceAsync(CancellationToken cancellationToken)
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped("directory team mirror");

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ITeamStore>();
        return await store.GetMaxDirectorySequenceAsync(cancellationToken);
    }

    // The mediator is scoped (its handlers' unit of work is), and the feed runs outside any request, so the command is
    // sent in its own scope.
    private async ValueTask SendAsync(
        TeamChangedCommand command,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.SendAsync(command, cancellationToken);
    }
}
