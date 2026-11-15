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
// See ADR-1411, ADR-1169 and ADR-1412.
internal sealed class DefaultPlatformDocumentApplier(
    IStoreManagerFactory storeManagerFactory,
    IServiceScopeFactory serviceScopeFactory,
    IAmbientTenantAccessor ambientTenantAccessor
) : IPlatformDocumentApplier
{
    /// <summary>
    /// The reason that the applier gives when it opens an unscoped ambient tenant scope to read and write the platform.
    /// </summary>
    private const string UnscopedReason = "directory platform mirror";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public async ValueTask ApplyAsync(
        PlatformDocument document,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

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
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IPlatformStore>();

        var persistedPlatform = await store.GetOrDefaultAsync(cancellationToken);
        return persistedPlatform?.DirectorySequence ?? 0;
    }

    /// <summary>
    /// Sends a platform-settings-changed command in its own service scope. The mediator is scoped (its handlers' unit
    /// of work is), and the feed runs outside any request.
    /// </summary>
    /// <param name="command">The <see cref="PlatformSettingsChangedCommand"/> to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
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
