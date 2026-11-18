// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.Logging;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Teams;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="ITeamDocumentApplier"/>. It mirrors a published team document into
/// the team's row, or removes the row of a retired team, sends a <see cref="TeamChangedCommand"/> so caches evict the
/// team, and has <see cref="ITeamRootKeyMirror"/> bring the team's owner root key to the committed row. A team that
/// tenants or teams still name is disabled instead of removed, because the cell refuses to delete a team that something
/// names, and its row records the retirement's sequence. A retirement whose sequence the team's row already carries or
/// exceeds changes nothing, so a change that the directory delivers again is applied once. The team row is committed
/// before the command and the root key follow, each in a step of its own on a token bounded by
/// <see cref="PostCommitWork.Timeout"/>, so the caller's cancellation drops neither, and a command or a root key that
/// fails or that the bound cuts is logged and never fails the committed write. A document whose parent team the cell
/// does not mirror writes nothing and is logged; the directory lists the team again after its parent's latest change,
/// which applies it.
/// </summary>
// See ADR-1351, ADR-1411 and ADR-1416.
internal sealed class DefaultTeamDocumentApplier(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    IPostCommitCommandSender postCommitCommandSender,
    ITeamRootKeyMirror teamRootKeyMirror,
    ILogger<DefaultTeamDocumentApplier> logger
) : ITeamDocumentApplier
{
    /// <summary>
    /// The reason that the applier gives when it opens an unscoped ambient tenant scope to read and write teams.
    /// </summary>
    private const string UnscopedReason = "directory team mirror";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private IPostCommitCommandSender PostCommitCommandSender { get; } = postCommitCommandSender;
    private ITeamRootKeyMirror TeamRootKeyMirror { get; } = teamRootKeyMirror;
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

            if (
                document.ParentTeamId is { } parentTeamId
                && await store.GetOrDefaultAsync(parentTeamId, cancellationToken) is null
            )
            {
                Logger.DirectoryTeamParentNotMirrored(document.TeamId, parentTeamId);
                return;
            }

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
            var teamChanged = new TeamChangedCommand(document.TeamId, settingsChanged);
            await PostCommitCommandSender.SendAsync(
                teamChanged,
                exception => Logger.DirectoryTeamChangeNotSent(document.TeamId, exception),
                cancellationToken
            );

            // The mirror reads the committed row, so it runs after the command, on a bounded token of its own.
            await TeamRootKeyMirror.MirrorAsync(document.TeamId, cancellationToken);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A team whose row carries the retirement's sequence or a later one changes nothing, so a retirement that the
    /// directory delivers again is not written, announced or logged twice. A team that something names is disabled with
    /// the retirement's sequence in one store command, which records the sequence on its row.
    /// </remarks>
    public async ValueTask RetireAsync(
        string teamId,
        long sequence,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            var store = storeManager.GetStore<ITeamStore>();

            var persistedTeam = await store.GetOrDefaultAsync(teamId, cancellationToken);
            if (persistedTeam is null || persistedTeam.DirectorySequence >= sequence)
                return;

            if (await store.HasDependentsAsync(teamId, cancellationToken))
            {
                if (!await store.DisableRetiredAsync(teamId, sequence, cancellationToken))
                    return;

                Logger.DirectoryRetiredTeamHasDependents(teamId, sequence);
            }
            else
            {
                await store.RemoveAsync(teamId, cancellationToken);
            }

            await storeManager.SaveChangesAsync(cancellationToken);
        }

        var teamChanged = new TeamChangedCommand(teamId, false);
        await PostCommitCommandSender.SendAsync(
            teamChanged,
            exception => Logger.DirectoryTeamChangeNotSent(teamId, exception),
            cancellationToken
        );

        // The mirror removes the owner root key of a team that has no row, and leaves the key of a disabled team.
        await TeamRootKeyMirror.MirrorAsync(teamId, cancellationToken);
    }
}
