// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Setup.Logging;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// Provides a default implementation of <see cref="ISignInRecoveryService"/>. In the anchor tenant the service removes
/// the tenant's own values of every sign-in-critical setting, and in the control-plane cell the platform's too, then
/// enables the tenant's default local connection, the clients that the setup options name and the management resource
/// server. The changes are staged on one store manager and saved once, then each changed resource is audited.
/// </summary>
// See ADR-1536.
internal sealed class DefaultSignInRecoveryService(
    IStoreManagerFactory storeManagerFactory,
    ITenantResolver tenantResolver,
    IServiceScopeFactory serviceScopeFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    ISettingDescriptorCollectionProvider settingDescriptorCollectionProvider,
    IPlatformTopology platformTopology,
    IPlatformDirectory platformDirectory,
    IOptions<SetupOptions> setupOptions,
    IManagementAuditRecorder managementAuditRecorder,
    ILogger<DefaultSignInRecoveryService> logger
) : ISignInRecoveryService
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private ITenantResolver TenantResolver { get; } = tenantResolver;

    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    private ISettingDescriptorCollectionProvider SettingDescriptorCollectionProvider { get; } =
        settingDescriptorCollectionProvider;

    private IPlatformTopology PlatformTopology { get; } = platformTopology;

    private IPlatformDirectory PlatformDirectory { get; } = platformDirectory;

    private SetupOptions SetupOptions { get; } = setupOptions.Value;

    private IManagementAuditRecorder ManagementAuditRecorder { get; } = managementAuditRecorder;

    private ILogger<DefaultSignInRecoveryService> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask<SignInRepairResult> RepairAsync(CancellationToken cancellationToken)
    {
        var criticalNames = SettingDescriptorCollectionProvider
            .Collection.Where(descriptor => descriptor.IsSignInCritical)
            .Select(descriptor => descriptor.Name)
            .ToList();

        // The anchor tenant is resolved in a scope of its own, as setup resolves it, and the scope stays open for the
        // audit records, which resolve the tenant's environment from its services.
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var tenant = await TenantResolver.ResolveControlPlaneTenantAsync(
            httpContext,
            cancellationToken
        );
        var tenantId = tenant.TenantId;

        using var unscopedScope = AmbientTenantAccessor.BeginUnscoped("sign-in recovery");

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var staged = new List<StagedChange>();
        JsonElement? platformSettings = null;

        if (PlatformTopology.IsControlPlaneCell)
        {
            platformSettings = await StagePlatformSettingsAsync(
                storeManager,
                criticalNames,
                staged,
                cancellationToken
            );
        }

        using (AmbientTenantAccessor.BeginScope(tenantId))
        {
            await StageTenantSettingsAsync(
                storeManager,
                tenantId,
                criticalNames,
                staged,
                cancellationToken
            );
            await StageConnectionAsync(storeManager, staged, cancellationToken);
            await StageClientsAsync(storeManager, staged, cancellationToken);
            await StageResourceServerAsync(storeManager, staged, cancellationToken);

            if (staged.Count > 0)
                await storeManager.SaveChangesAsync(cancellationToken);
        }

        if (platformSettings is { } settings)
            await TryPublishAsync(settings, cancellationToken);

        await RecordAuditAsync(httpContext, tenant, staged);

        foreach (var item in staged)
        {
            Logger.SignInRepaired(item.Change.Scope, item.Change.Name, item.Change.Before);
        }

        return new SignInRepairResult { Changes = [.. staged.Select(item => item.Change)] };
    }

    private static JsonObject? ReadObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return null;

        return JsonSerializer.Deserialize<JsonObject>(value);
    }

    // Removes the sign-in-critical properties and returns the document that remains, or null when none is present.
    private static JsonElement? RemoveCriticalSettings(
        JsonElement value,
        IReadOnlyList<string> criticalNames,
        string scope,
        string resourceId,
        List<StagedChange> staged
    )
    {
        var document = ReadObject(value);
        if (document is null)
            return null;

        var isChanged = false;
        foreach (var name in criticalNames)
        {
            if (!document.TryGetPropertyValue(name, out var before))
                continue;

            var beforeText = before?.ToJsonString() ?? "null";
            document.Remove(name);
            isChanged = true;

            var change = new SignInRepairChange
            {
                Scope = scope,
                Name = name,
                Before = beforeText,
            };
            var item = new StagedChange(change, resourceId);
            staged.Add(item);
        }

        if (!isChanged)
            return null;

        return JsonSerializer.SerializeToElement(document);
    }

    private static async ValueTask<JsonElement?> StagePlatformSettingsAsync(
        IStoreManager storeManager,
        IReadOnlyList<string> criticalNames,
        List<StagedChange> staged,
        CancellationToken cancellationToken
    )
    {
        var store = storeManager.GetStore<IPlatformStore>();
        var settings = await store.GetSettingsOrDefaultAsync(cancellationToken);
        if (settings is null)
            return null;

        var updated = RemoveCriticalSettings(
            settings.Value,
            criticalNames,
            SignInRepairScopes.Platform,
            OpenIdConstants.PlatformResourceId,
            staged
        );
        if (updated is not { } value)
            return null;

        settings.Value = value;
        await store.UpdateSettingsAsync(settings, cancellationToken);
        return value;
    }

    private static async ValueTask StageTenantSettingsAsync(
        IStoreManager storeManager,
        string tenantId,
        IReadOnlyList<string> criticalNames,
        List<StagedChange> staged,
        CancellationToken cancellationToken
    )
    {
        var store = storeManager.GetStore<ITenantStore>();
        var settings = await store.GetSettingsOrDefaultAsync(tenantId, cancellationToken);
        if (settings is null)
            return;

        var updated = RemoveCriticalSettings(
            settings.Value,
            criticalNames,
            SignInRepairScopes.Tenant,
            tenantId,
            staged
        );
        if (updated is not { } value)
            return;

        settings.Value = value;
        await store.UpdateSettingsAsync(settings, cancellationToken);
    }

    private static async ValueTask StageConnectionAsync(
        IStoreManager storeManager,
        List<StagedChange> staged,
        CancellationToken cancellationToken
    )
    {
        var store = storeManager.GetStore<IConnectionStore>();
        var connection = await store.GetByNameOrDefaultAsync(
            ConnectionDefaults.Name,
            cancellationToken
        );
        if (connection is null || connection.IsEnabled)
            return;

        connection.IsEnabled = true;
        await store.UpdateAsync(connection, cancellationToken);

        var change = new SignInRepairChange
        {
            Scope = SignInRepairScopes.Connection,
            Name = connection.Name,
        };
        var item = new StagedChange(change, connection.ConnectionId);
        staged.Add(item);
    }

    private async ValueTask StageClientsAsync(
        IStoreManager storeManager,
        List<StagedChange> staged,
        CancellationToken cancellationToken
    )
    {
        var clientIds = new HashSet<string>(StringComparer.Ordinal);
        var consoleClientId = SetupOptions.ManagementConsole.ClientId;
        if (!string.IsNullOrWhiteSpace(consoleClientId))
            clientIds.Add(consoleClientId);

        var automationClientId = SetupOptions.Automation.ClientId;
        if (!string.IsNullOrWhiteSpace(automationClientId))
            clientIds.Add(automationClientId);

        var store = storeManager.GetStore<IClientStore>();
        foreach (var clientId in clientIds)
        {
            var client = await store.GetOrDefaultAsync(clientId, cancellationToken);
            if (client is null || !client.IsDisabled)
                continue;

            client.IsDisabled = false;
            await store.UpdateAsync(client, cancellationToken);

            var change = new SignInRepairChange
            {
                Scope = SignInRepairScopes.Client,
                Name = clientId,
            };
            var item = new StagedChange(change, clientId);
            staged.Add(item);
        }
    }

    private static async ValueTask StageResourceServerAsync(
        IStoreManager storeManager,
        List<StagedChange> staged,
        CancellationToken cancellationToken
    )
    {
        var store = storeManager.GetStore<IResourceServerStore>();
        var resourceServer = await store.GetByIdentifierOrDefaultAsync(
            OpenIdConstants.SystemResourceServerIdentifiers.Management,
            cancellationToken
        );
        if (resourceServer is null || !resourceServer.IsDisabled)
            return;

        resourceServer.IsDisabled = false;
        await store.UpdateAsync(resourceServer, cancellationToken);

        var change = new SignInRepairChange
        {
            Scope = SignInRepairScopes.ResourceServer,
            Name = resourceServer.Identifier,
        };
        var item = new StagedChange(change, resourceServer.ResourceServerId);
        staged.Add(item);
    }

    // A directory that cannot be reached leaves the saved platform settings unpublished, which the log reports.
    private async ValueTask TryPublishAsync(
        JsonElement settings,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var document = new PlatformDocument { Settings = settings.Clone() };
            await PlatformDirectory.PublishAsync(document, cancellationToken);
        }
        catch (DirectoryUnavailableException exception)
        {
            Logger.SignInRepairNotPublished(exception);
        }
    }

    [SuppressMessage(
        "Design",
        "NCID0003",
        Justification = "The record follows a committed change and runs on a bounded token of the recorder's own, so no caller can pass a token that drops it (ADR-1351)."
    )]
    private async ValueTask RecordAuditAsync(
        HttpContext httpContext,
        PersistedTenant tenant,
        List<StagedChange> staged
    )
    {
        var tenantId = tenant.TenantId;
        var recorded = new HashSet<(string Scope, string ResourceId)>();
        foreach (var item in staged)
        {
            var scope = item.Change.Scope;
            var resourceId = item.ResourceId;
            if (!recorded.Add((scope, resourceId)))
                continue;

            switch (scope)
            {
                case SignInRepairScopes.Platform:
                    await ManagementAuditRecorder.RecordPlatformChangedAsync(
                        httpContext,
                        ResourceChangeTypes.SettingsUpdated,
                        null
                    );
                    break;
                case SignInRepairScopes.Tenant:
                    await ManagementAuditRecorder.RecordTenantChangedAsync(
                        httpContext,
                        tenant.TeamId,
                        tenantId,
                        ResourceChangeTypes.SettingsUpdated,
                        null
                    );
                    break;
                case SignInRepairScopes.Connection:
                    await ManagementAuditRecorder.RecordConnectionChangedAsync(
                        httpContext,
                        tenantId,
                        resourceId,
                        ResourceChangeTypes.Updated,
                        null
                    );
                    break;
                case SignInRepairScopes.Client:
                    await ManagementAuditRecorder.RecordClientChangedAsync(
                        httpContext,
                        tenantId,
                        resourceId,
                        ResourceChangeTypes.Updated,
                        null
                    );
                    break;
                case SignInRepairScopes.ResourceServer:
                    await ManagementAuditRecorder.RecordResourceServerChangedAsync(
                        httpContext,
                        tenantId,
                        resourceId,
                        ResourceChangeTypes.Updated,
                        null
                    );
                    break;
            }
        }
    }

    private sealed record StagedChange(SignInRepairChange Change, string ResourceId);
}
