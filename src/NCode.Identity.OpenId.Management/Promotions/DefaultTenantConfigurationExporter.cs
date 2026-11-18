// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Endpoints.Clients;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Promotions;

/// <summary>
/// Provides a default implementation of <see cref="ITenantConfigurationExporter"/> that reads a tenant's settings,
/// resource servers, clients and client grants under the tenant's own scope. It leaves out machine clients, clients the
/// host manages and the grants of those clients, as the promotion contributors do, and it reads no secret.
/// </summary>
// See ADR-1425.
internal class DefaultTenantConfigurationExporter(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    IHostManagedClientChecker hostManagedClientChecker,
    IOptions<CellOptions> cellOptions
) : ITenantConfigurationExporter
{
    private const int PageLimit = 100;

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    private IHostManagedClientChecker HostManagedClientChecker { get; } = hostManagedClientChecker;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    public async ValueTask<TenantConfigurationDocument?> ExportAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        using var scope = AmbientTenantAccessor.BeginScope(tenantId);

        var tenant = await storeManager
            .GetStore<ITenantStore>()
            .GetOrDefaultAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var clients = new List<TenantConfigurationClient>();
        var clientGrants = new List<TenantConfigurationClientGrant>();
        await ReadClientsAsync(storeManager, tenantId, clients, clientGrants, cancellationToken);

        var resourceServers = await ReadResourceServersAsync(storeManager, cancellationToken);

        return new TenantConfigurationDocument
        {
            SourceTenantId = tenant.TenantId,
            Region = CellOptions.Region,
            Settings = tenant.Settings.Value,
            Clients = clients,
            ClientGrants = clientGrants,
            ResourceServers = resourceServers,
        };
    }

    // A machine client belongs to its owning node, not to the tenant's configuration (ADR-1466), and the host
    // configures a host-managed client, so neither the client nor its grants are exported.
    private async ValueTask ReadClientsAsync(
        IStoreManager storeManager,
        string tenantId,
        List<TenantConfigurationClient> clients,
        List<TenantConfigurationClientGrant> clientGrants,
        CancellationToken cancellationToken
    )
    {
        var clientStore = storeManager.GetStore<IClientStore>();
        var grantStore = storeManager.GetStore<IClientGrantStore>();

        string? clientCursor = null;
        do
        {
            var clientPage = await clientStore.GetPageAsync(
                new PageRequest { Cursor = clientCursor, Limit = PageLimit },
                cancellationToken
            );

            foreach (var client in clientPage.Items)
            {
                if (
                    client.OwningNodeType is not null
                    || HostManagedClientChecker.IsHostManaged(tenantId, client.ClientId)
                )
                {
                    continue;
                }

                clients.Add(
                    new TenantConfigurationClient
                    {
                        ClientId = client.ClientId,
                        IsDisabled = client.IsDisabled,
                        ResourceServerId = client.ResourceServerId,
                        Settings = client.Settings.Value,
                    }
                );

                await ReadClientGrantsAsync(
                    grantStore,
                    client.ClientId,
                    clientGrants,
                    cancellationToken
                );
            }

            clientCursor = clientPage.NextCursor;
        } while (clientCursor is not null);
    }

    private static async ValueTask ReadClientGrantsAsync(
        IClientGrantStore grantStore,
        string clientId,
        List<TenantConfigurationClientGrant> clientGrants,
        CancellationToken cancellationToken
    )
    {
        string? grantCursor = null;
        do
        {
            var grantPage = await grantStore.GetPageAsync(
                clientId,
                new PageRequest { Cursor = grantCursor, Limit = PageLimit },
                cancellationToken
            );

            clientGrants.AddRange(
                grantPage.Items.Select(grant => new TenantConfigurationClientGrant
                {
                    ClientId = clientId,
                    ResourceServerId = grant.ResourceServerId,
                    Scopes = [.. grant.Scopes],
                })
            );

            grantCursor = grantPage.NextCursor;
        } while (grantCursor is not null);
    }

    private static async ValueTask<
        List<TenantConfigurationResourceServer>
    > ReadResourceServersAsync(IStoreManager storeManager, CancellationToken cancellationToken)
    {
        var store = storeManager.GetStore<IResourceServerStore>();
        var resourceServers = new List<TenantConfigurationResourceServer>();

        string? cursor = null;
        do
        {
            var page = await store.GetPageAsync(
                new PageRequest { Cursor = cursor, Limit = PageLimit },
                cancellationToken
            );

            resourceServers.AddRange(
                page.Items.Select(resourceServer => new TenantConfigurationResourceServer
                {
                    ResourceServerId = resourceServer.ResourceServerId,
                    Identifier = resourceServer.Identifier,
                    Name = resourceServer.Name,
                    IsSystem = resourceServer.IsSystem,
                    IsDisabled = resourceServer.IsDisabled,
                    Settings = resourceServer.Settings.Value,
                    Scopes =
                    [
                        .. resourceServer.Scopes.Select(scope => new TenantConfigurationScope
                        {
                            Value = scope.Value,
                            Description = scope.Description,
                            IsSystem = scope.IsSystem,
                        }),
                    ],
                })
            );

            cursor = page.NextCursor;
        } while (cursor is not null);

        return resourceServers;
    }
}
