// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Platform;
using NCode.Identity.Persistence.Stores;
using NCode.Mediator;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="IPlatformDocumentApplier"/>. It mirrors a published platform
/// document into the platform row, then sends a <see cref="PlatformSettingsChangedCommand"/> that carries the settings
/// before and after, so caches evict the settings.
/// </summary>
// See ADR-1169 and ADR-1412.
internal sealed class DefaultPlatformDocumentApplier(
    IStoreManagerFactory storeManagerFactory,
    IServiceScopeFactory serviceScopeFactory,
    IAmbientTenantAccessor ambientTenantAccessor
) : IPlatformDocumentApplier
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public async ValueTask ApplyAsync(
        PlatformDocument document,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped("directory platform mirror");

        var settings = JsonElement.Parse(document.SettingsJson);
        var persistedSettings = new PersistedPlatformSettings
        {
            ConcurrencyToken = string.Empty,
            Value = settings,
        };

        JsonElement previousSettings;
        bool isApplied;
        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            var store = storeManager.GetStore<IPlatformStore>();

            var storedSettings = await store.GetSettingsOrDefaultAsync(cancellationToken);
            previousSettings = storedSettings?.Value ?? JsonElement.Parse("{}");

            isApplied = await store.MirrorSettingsAsync(
                persistedSettings,
                document.Sequence,
                cancellationToken
            );
            if (isApplied)
            {
                await storeManager.SaveChangesAsync(cancellationToken);
            }
        }

        if (isApplied)
        {
            await SendAsync(
                new PlatformSettingsChangedCommand(previousSettings.Clone(), settings.Clone()),
                cancellationToken
            );
        }
    }

    /// <inheritdoc />
    public async ValueTask<long> GetAppliedSequenceAsync(CancellationToken cancellationToken)
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped("directory platform mirror");

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IPlatformStore>();

        var persistedPlatform = await store.GetOrDefaultAsync(cancellationToken);
        return persistedPlatform?.DirectorySequence ?? 0;
    }

    // The mediator is scoped (its handlers' unit of work is), and the feed runs outside any request, so the command is
    // sent in its own scope.
    private async ValueTask SendAsync(
        PlatformSettingsChangedCommand command,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.SendAsync(command, cancellationToken);
    }
}
