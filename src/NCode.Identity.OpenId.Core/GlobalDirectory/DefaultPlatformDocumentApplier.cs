// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Platform;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="IPlatformDocumentApplier"/>. It mirrors a published platform
/// document into the platform row, then sends a <see cref="PlatformSettingsChangedCommand"/> that carries the settings
/// before and after, so caches evict the settings. The platform row is committed before the command, which runs on its
/// own bounded token, so a command that fails is logged and never fails the committed write.
/// </summary>
// See ADR-1411, ADR-1412 and ADR-1351.
internal sealed class DefaultPlatformDocumentApplier(
    IStoreManagerFactory storeManagerFactory,
    IServiceScopeFactory serviceScopeFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    TimeProvider timeProvider,
    ILogger<DefaultPlatformDocumentApplier> logger
) : IPlatformDocumentApplier
{
    /// <summary>
    /// The reason that the applier gives when it opens an unscoped ambient tenant scope to read and write the platform.
    /// </summary>
    private const string UnscopedReason = "directory platform mirror";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private ILogger<DefaultPlatformDocumentApplier> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask ApplyAsync(
        PlatformDocument document,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var settings = document.Settings;
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
            previousSettings = storedSettings?.Value ?? JsonElements.EmptyObject;

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
            await PostCommitCommandSender.SendAsync(
                ServiceScopeFactory,
                TimeProvider,
                new PlatformSettingsChangedCommand(previousSettings.Clone(), settings.Clone()),
                exception => Logger.DirectoryPlatformChangeNotSent(exception),
                cancellationToken
            );
        }
    }
}
