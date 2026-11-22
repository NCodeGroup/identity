// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="ITeamRootKeyMirror"/>. It mirrors the root key that a team's row
/// holds for the cell's region into the team's owner root key, which rewraps the master keys of the team's tenants in
/// this cell. Mirroring one team reads its committed row and brings the owner root key to the row's reference, unless
/// the team is disabled: a disabled team keeps the owner root key it has, and the document that enables it again
/// mirrors its key. A team that has no row has its owner root key removed, and a team disabled because something names
/// it keeps its key. The mirror follows the committed row, so it runs in a step of its own on a token bounded by
/// <see cref="PostCommitWork.Timeout"/>, which the caller's cancellation never reaches. A key that fails, or that the
/// bound cuts, is logged and never thrown, and the next document for the team or the next reconcile retries it.
/// Reconciling brings the owner root key of every enabled mirrored team to the reference its row holds for the region,
/// which applies a key that failed its probe or its rotation cooldown once it can be, and removes the owner root key of
/// a team that has no row. A cancellation of the caller's own token propagates from both.
/// </summary>
// See ADR-1351, ADR-1411 and ADR-1416.
internal sealed class DefaultTeamRootKeyMirror(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    IOwnerRootKeyManager ownerRootKeyManager,
    IOptions<CellOptions> cellOptionsAccessor,
    TimeProvider timeProvider,
    ILogger<DefaultTeamRootKeyMirror> logger
) : ITeamRootKeyMirror
{
    /// <summary>
    /// The reason that the mirror gives when it opens an unscoped ambient tenant scope to read teams and their owner
    /// root keys.
    /// </summary>
    private const string UnscopedReason = "directory team root key mirror";

    /// <summary>
    /// The number of teams or owner root keys that the reconcile reads in one page, so it never holds every team or key
    /// at once.
    /// </summary>
    private const int PageSize = 100;

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private IOwnerRootKeyManager OwnerRootKeyManager { get; } = ownerRootKeyManager;
    private CellOptions CellOptions { get; } = cellOptionsAccessor.Value;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private ILogger<DefaultTeamRootKeyMirror> Logger { get; } = logger;

    /// <inheritdoc />
    public ValueTask MirrorAsync(string teamId, CancellationToken cancellationToken) =>
        RunRootKeyWorkAsync(
            teamId,
            () =>
                PostCommitWork.RunAsync(
                    TimeProvider,
                    static async (state, token) =>
                        await state.Mirror.MirrorOnceAsync(state.TeamId, token),
                    (Mirror: this, TeamId: teamId)
                ),
            cancellationToken
        );

    /// <inheritdoc />
    public async ValueTask ReconcileAsync(CancellationToken cancellationToken)
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
                var enabledTeamIds = enabledTeams.ConvertAll(team => team.TeamId);
                var currentKeys = (
                    await OwnerRootKeyManager.GetManyAsync(
                        SecretOwnerTypes.Team,
                        enabledTeamIds,
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
    /// Reads the committed row of a team and the owner root key in place, then brings the key to the reference that the
    /// row holds for the cell's region, as <see cref="ApplyRootKeyAsync"/> does. A disabled team keeps the owner root
    /// key it has, as <see cref="ReconcileAsync"/> leaves it, and a team that has no row has its key removed. A failure
    /// propagates to the caller.
    /// </summary>
    /// <param name="teamId">The identifier of the team whose owner root key is mirrored.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    private async ValueTask MirrorOnceAsync(string teamId, CancellationToken cancellationToken)
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        PersistedTeam? row;
        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            var store = storeManager.GetStore<ITeamStore>();
            row = await store.GetOrDefaultAsync(teamId, cancellationToken);
        }

        // A disabled team keeps its owner root key, as ReconcileAsync leaves it.
        if (row is { IsDisabled: true })
            return;

        var owner = new SecretOwner(SecretOwnerTypes.Team, teamId).Normalize();
        var current = await OwnerRootKeyManager.GetAsync(owner, cancellationToken);
        var regionRootKey = row?.RootKeys.GetValueOrDefault(CellOptions.Region);
        await ApplyRootKeyAsync(owner, regionRootKey, current, cancellationToken);
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
                string[] ownerIds = [.. keys.Items.Select(key => key.Owner.OwnerId)];
                var existingTeamIds = await GetExistingTeamIdsAsync(ownerIds, cancellationToken);
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
    /// Brings the owner root key of a team to the reference that the team's row holds for the cell's region: it removes
    /// the key when the row holds none, sets it when it differs from the key in place, and leaves it when it is the
    /// same. A failure propagates to the caller.
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
    /// Runs the work of one team's owner root key. A failure is logged and never fails the committed write or stops the
    /// next team; a caller's cancellation propagates.
    /// </summary>
    /// <param name="teamId">The identifier of the team whose owner root key the work changes.</param>
    /// <param name="work">The work to run.</param>
    /// <param name="cancellationToken">
    /// The <see cref="CancellationToken"/> of the caller, whose cancellation propagates.
    /// </param>
    private ValueTask RunRootKeyWorkAsync(
        string teamId,
        Func<ValueTask> work,
        CancellationToken cancellationToken
    ) =>
        IsolatedStep.RunAsync(
            work,
            exception =>
                Logger.DirectoryTeamRootKeyNotApplied(teamId, CellOptions.Region, exception),
            cancellationToken
        );
}
