// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Provides the default implementation of <see cref="IConnectionResolver"/> over the tenant's
/// <see cref="IConnectionStore"/> and the registered <see cref="IConnectionStrategyCatalog"/>.
/// </summary>
// See ADR-1535.
internal sealed class DefaultConnectionResolver(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    IConnectionStrategyCatalog strategyCatalog,
    IEffectiveSettingsResolver effectiveSettingsResolver
) : IConnectionResolver
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private IConnectionStrategyCatalog StrategyCatalog { get; } = strategyCatalog;
    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;

    /// <inheritdoc />
    public async ValueTask<ResolvedConnection?> GetByIdOrDefaultAsync(
        OpenIdContext openIdContext,
        string connectionId,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(openIdContext);
        ArgumentException.ThrowIfNullOrEmpty(connectionId);

        using var tenantScope = AmbientTenantAccessor.BeginScope(openIdContext.Tenant.TenantId);
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IConnectionStore>();

        var connection = await store.GetOrDefaultAsync(connectionId, cancellationToken);
        return connection is null ? null : Resolve(openIdContext, connection);
    }

    /// <inheritdoc />
    public async ValueTask<ResolvedConnection?> GetByNameOrDefaultAsync(
        OpenIdContext openIdContext,
        string name,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(openIdContext);
        ArgumentException.ThrowIfNullOrEmpty(name);

        using var tenantScope = AmbientTenantAccessor.BeginScope(openIdContext.Tenant.TenantId);
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IConnectionStore>();

        var connection = await store.GetByNameOrDefaultAsync(name, cancellationToken);
        return connection is null ? null : Resolve(openIdContext, connection);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ResolvedConnection>> GetAllowedAsync(
        OpenIdContext openIdContext,
        IReadOnlySettingCollection clientSettings,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(openIdContext);
        ArgumentNullException.ThrowIfNull(clientSettings);

        using var tenantScope = AmbientTenantAccessor.BeginScope(openIdContext.Tenant.TenantId);
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IConnectionStore>();

        var connections = await store.GetAllAsync(cancellationToken);

        var allowed = new List<ResolvedConnection>();
        foreach (var connection in connections)
        {
            if (!IsAllowed(clientSettings, connection))
                continue;

            var resolved = Resolve(openIdContext, connection);
            if (resolved is not null)
                allowed.Add(resolved);
        }

        return allowed.OrderBy(item => item.Name, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<ConnectionSelection> SelectAsync(
        OpenIdContext openIdContext,
        IReadOnlySettingCollection clientSettings,
        string? requestedName,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(openIdContext);
        ArgumentNullException.ThrowIfNull(clientSettings);

        using var tenantScope = AmbientTenantAccessor.BeginScope(openIdContext.Tenant.TenantId);
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IConnectionStore>();

        if (!string.IsNullOrEmpty(requestedName))
        {
            var requested = await FindUsableAsync(
                openIdContext,
                store,
                clientSettings,
                requestedName,
                cancellationToken
            );

            return requested is null
                ? new ConnectionSelection
                {
                    ErrorDescription = "The requested connection is not available to the client.",
                }
                : new ConnectionSelection { Connection = requested };
        }

        var defaultName = clientSettings.GetValue(OpenIdSettingKeys.DefaultConnection);
        var selected = string.IsNullOrEmpty(defaultName)
            ? null
            : await FindUsableAsync(
                openIdContext,
                store,
                clientSettings,
                defaultName,
                cancellationToken
            );

        return new ConnectionSelection { Connection = selected };
    }

    private async ValueTask<ResolvedConnection?> FindUsableAsync(
        OpenIdContext openIdContext,
        IConnectionStore store,
        IReadOnlySettingCollection clientSettings,
        string name,
        CancellationToken cancellationToken
    )
    {
        var connection = await store.GetByNameOrDefaultAsync(name, cancellationToken);
        return connection is not null && IsAllowed(clientSettings, connection)
            ? Resolve(openIdContext, connection)
            : null;
    }

    private static bool IsAllowed(
        IReadOnlySettingCollection clientSettings,
        PersistedConnection connection
    )
    {
        if (!connection.IsEnabled)
            return false;

        return !clientSettings.TryGetValue(OpenIdSettingKeys.AllowedConnections, out var allowed)
            || allowed.Contains(connection.Name, StringComparer.OrdinalIgnoreCase);
    }

    private ResolvedConnection? Resolve(OpenIdContext openIdContext, PersistedConnection connection)
    {
        if (!StrategyCatalog.TryGetStrategy(connection.Strategy, out var strategy))
            return null;

        var settings = EffectiveSettingsResolver.ResolveStored(
            openIdContext.Tenant.SettingsProvider.Collection,
            OpenIdSettingScopes.Connection,
            connection.ConnectionId,
            connection.Settings.Value
        );

        return new ResolvedConnection
        {
            Connection = connection,
            Strategy = strategy,
            Settings = settings,
        };
    }
}
