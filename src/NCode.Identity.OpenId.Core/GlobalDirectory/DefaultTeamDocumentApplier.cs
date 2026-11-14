// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// delete a team that something names.
/// </summary>
// See ADR-1411 and ADR-1416.
internal sealed class DefaultTeamDocumentApplier(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<DefaultTeamDocumentApplier> logger
) : ITeamDocumentApplier
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;
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
