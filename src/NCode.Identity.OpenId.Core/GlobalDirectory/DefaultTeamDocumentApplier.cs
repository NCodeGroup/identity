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

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="ITeamDocumentApplier"/>. It mirrors a published team document
/// into the team's row, or removes the row of a retired team, then sends a <see cref="TeamChangedCommand"/> so caches
/// evict the team. A team that tenants or teams still name is disabled instead of removed, because the cell refuses to
/// delete a team that something names. The root key that the document holds for the cell's region is mirrored into the
/// team's owner root key, which rewraps the master keys of the team's tenants in this cell, unless the team is
/// disabled: a disabled team keeps the owner root key it has, and the document that enables it again mirrors its key.
/// A removed team's owner root key is removed, and a team disabled because something names it keeps its key; a
/// removed team's key that fails to go is removed by the next reconcile. Reconciling brings the owner root key of
/// every enabled mirrored team to the reference its row holds for the region, which applies a key that failed its
/// probe or its rotation cooldown once it can be, and removes the owner root key of a team that has no row. The team
/// row is committed before the command and the root key follow, so a command or a root key that fails is logged and
/// never fails the committed write.
/// </summary>
// See ADR-1411 and ADR-1416.
internal sealed class DefaultTeamDocumentApplier(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    IServiceScopeFactory serviceScopeFactory,
    IOwnerRootKeyManager ownerRootKeyManager,
    IOptions<CellOptions> cellOptionsAccessor,
    TimeProvider timeProvider,
    ILogger<DefaultTeamDocumentApplier> logger
) : ITeamDocumentApplier
{
    /// <summary>
    /// The reason that the applier gives when it opens an unscoped ambient tenant scope to read and write teams.
    /// </summary>
    private const string UnscopedReason = "directory team mirror";

    /// <summary>
    /// The number of teams or owner root keys that the reconcile reads in one page, so it never holds every team or key
    /// at once.
    /// </summary>
    private const int PageSize = 100;

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;
    private IOwnerRootKeyManager OwnerRootKeyManager { get; } = ownerRootKeyManager;
    private CellOptions CellOptions { get; } = cellOptionsAccessor.Value;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private ILogger<DefaultTeamDocumentApplier> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask ApplyAsync(TeamDocument document, CancellationToken cancellationToken)
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var settings = document.Settings;
        var persistedTeam = new PersistedTeam
        {
            TeamId = document.TeamId,
            ParentTeamId = document.ParentTeamId,
            IsDisabled = document.IsDisabled,
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
            await PostCommitCommandSender.SendAsync(
                ServiceScopeFactory,
                TimeProvider,
                new TeamChangedCommand(document.TeamId, settingsChanged),
                exception => Logger.DirectoryTeamChangeNotSent(document.TeamId, exception),
                cancellationToken
            );

            // A disabled team keeps its owner root key, as ReconcileRootKeysAsync leaves it.
            if (document.IsDisabled)
                return;

            // The mirror stays on the caller's token: the rewrap must not be cut by the post-commit bound, and a mirror
            // that fails or is cancelled is redone by ReconcileRootKeysAsync on a replica's first pass.
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
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var region = CellOptions.Region;
        var page = new PageRequest { Limit = PageSize };
        do
        {
            var teams = await GetTeamPageAsync(page, cancellationToken);
            var enabledTeams = teams.Items.Where(team => !team.IsDisabled).ToList();
            if (enabledTeams.Count > 0)
            {
                var currentKeys = (
                    await OwnerRootKeyManager.GetManyAsync(
                        SecretOwnerTypes.Team,
                        enabledTeams.ConvertAll(team => team.TeamId),
                        cancellationToken
                    )
                ).ToDictionary(key => key.Owner.OwnerId, StringComparer.OrdinalIgnoreCase);

                foreach (var team in enabledTeams)
                {
                    var owner = new SecretOwner(SecretOwnerTypes.Team, team.TeamId).Normalize();
                    var reference = team.RootKeys.GetValueOrDefault(region);
                    var current = currentKeys.GetValueOrDefault(owner.OwnerId);
                    await RunRootKeyWorkAsync(
                        team.TeamId,
                        () => ApplyRootKeyAsync(owner, reference, current, cancellationToken),
                        cancellationToken
                    );
                }
            }

            page = page with { Cursor = teams.NextCursor };
        } while (page.Cursor is not null);

        await RemoveKeysOfGoneTeamsAsync(cancellationToken);
    }

    /// <summary>
    /// Removes the owner root key of every team owner that has no team row, which finishes a retirement whose key
    /// removal failed. A key whose removal is under way is left to the owner root key validation service.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    private async ValueTask RemoveKeysOfGoneTeamsAsync(CancellationToken cancellationToken)
    {
        var page = new PageRequest { Limit = PageSize };
        do
        {
            var keys = await OwnerRootKeyManager.GetPageAsync(
                SecretOwnerTypes.Team,
                page,
                cancellationToken
            );
            if (keys.Items.Count > 0)
            {
                var existingTeamIds = await GetExistingTeamIdsAsync(
                    [.. keys.Items.Select(key => key.Owner.OwnerId)],
                    cancellationToken
                );
                foreach (var key in keys.Items)
                {
                    if (existingTeamIds.Contains(key.Owner.OwnerId))
                        continue;

                    // The owner root key validation service completes a removal that is under way.
                    if (
                        string.Equals(
                            key.State,
                            OwnerRootKeyStates.Removing,
                            StringComparison.Ordinal
                        )
                    )
                        continue;

                    await RunRootKeyWorkAsync(
                        key.Owner.OwnerId,
                        async () =>
                            await OwnerRootKeyManager.RemoveAsync(key.Owner, cancellationToken),
                        cancellationToken
                    );
                }
            }

            page = page with { Cursor = keys.NextCursor };
        } while (page.Cursor is not null);
    }

    /// <summary>
    /// Reads which of the named teams have a row in this cell.
    /// </summary>
    /// <param name="teamIds">The identifiers of the teams to look up.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The identifiers, compared without regard to case, of the teams that have a row.</returns>
    private async ValueTask<HashSet<string>> GetExistingTeamIdsAsync(
        IReadOnlyCollection<string> teamIds,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ITeamStore>();
        var tokens = await store.GetConcurrencyTokensAsync(teamIds, cancellationToken);
        return new HashSet<string>(tokens.Keys, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads one page of the teams stored in this cell.
    /// </summary>
    /// <param name="page">The <see cref="PageRequest"/> that names the page to read.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="PagedResult{T}"/> of the teams on the page, with the cursor of the next page.</returns>
    private async ValueTask<PagedResult<PersistedTeam>> GetTeamPageAsync(
        PageRequest page,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ITeamStore>();
        return await store.GetPageAsync(page, cancellationToken);
    }

    /// <summary>
    /// Reads the owner root key of a team and brings it to the reference that the team's document holds for the cell's
    /// region, as <see cref="ApplyRootKeyAsync"/> does. A key that fails is logged and left unapplied, so one team's
    /// key never stops another's; the team row and its command are already applied, and the next document the
    /// directory publishes for the team, or the next reconcile, retries the key, and a removed team's key that fails
    /// to go is removed by the next reconcile. A caller's cancellation propagates.
    /// </summary>
    /// <param name="teamId">The identifier of the team whose owner root key is mirrored.</param>
    /// <param name="reference">The root key reference for the region, or <c>null</c> when the team has none.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    private async ValueTask MirrorRootKeyAsync(
        string teamId,
        OwnerRootKeyRequest? reference,
        CancellationToken cancellationToken
    )
    {
        var owner = new SecretOwner(SecretOwnerTypes.Team, teamId).Normalize();
        await RunRootKeyWorkAsync(
            teamId,
            async () =>
            {
                var current = await OwnerRootKeyManager.GetAsync(owner, cancellationToken);
                await ApplyRootKeyAsync(owner, reference, current, cancellationToken);
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Brings the owner root key of a team to the reference that the team's document holds for the cell's region: it
    /// removes the key when the document holds none, sets it when it differs from the key in place, and leaves it
    /// when it is the same. A failure propagates to the caller.
    /// </summary>
    /// <param name="owner">The normalized <see cref="SecretOwner"/> of the team.</param>
    /// <param name="reference">The root key reference for the region, or <c>null</c> when the team has none.</param>
    /// <param name="current">The owner root key in place, or <c>null</c> when the team has none.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    private async ValueTask ApplyRootKeyAsync(
        SecretOwner owner,
        OwnerRootKeyRequest? reference,
        OwnerRootKeyInfo? current,
        CancellationToken cancellationToken
    )
    {
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
            && string.Equals(current.KeyReference, reference.KeyReference, StringComparison.Ordinal)
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

    /// <summary>
    /// Runs the work of one team's owner root key. A failure is logged and never fails the committed write or stops
    /// the next team; a caller's cancellation propagates.
    /// </summary>
    /// <param name="teamId">The identifier of the team whose owner root key the work changes.</param>
    /// <param name="work">The work to run.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    private async ValueTask RunRootKeyWorkAsync(
        string teamId,
        Func<ValueTask> work,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await work();
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            Logger.DirectoryTeamRootKeyNotApplied(teamId, CellOptions.Region, exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask RetireAsync(
        string teamId,
        long sequence,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var isRemoved = false;
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
                isRemoved = true;
            }

            await storeManager.SaveChangesAsync(cancellationToken);
        }

        await PostCommitCommandSender.SendAsync(
            ServiceScopeFactory,
            TimeProvider,
            new TeamChangedCommand(teamId, false),
            exception => Logger.DirectoryTeamChangeNotSent(teamId, exception),
            cancellationToken
        );

        // A removed team's owner root key goes with it; a team disabled because something names it keeps its key.
        if (isRemoved)
            await MirrorRootKeyAsync(teamId, null, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<long> GetAppliedSequenceAsync(CancellationToken cancellationToken)
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ITeamStore>();
        return await store.GetMaxDirectorySequenceAsync(cancellationToken);
    }
}
