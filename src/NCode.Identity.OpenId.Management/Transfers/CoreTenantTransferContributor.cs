// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.RemoteKeys;

namespace NCode.Identity.OpenId.Management.Transfers;

/// <summary>
/// Provides an implementation of <see cref="ITenantTransferContributor"/> that carries a tenant's core records: the
/// tenant row and its settings, its connections, its clients with their secrets, its client grants, its resource
/// servers with their scopes and secrets, its own secrets and its tenant-level role assignments. Secret values stay as
/// their protected envelopes. Grants, consents, sessions, federated principals and identities are not carried, and the
/// audit and usage records stay in the cell that the tenant leaves.
/// </summary>
// See ADR-1514.
internal class CoreTenantTransferContributor(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor
) : ITenantTransferContributor
{
    /// <summary>
    /// The name of the section that the contributor writes.
    /// </summary>
    internal const string SectionName = "core";

    private const int PageLimit = 100;
    private const string TenantProperty = "tenant";
    private const string ConnectionsProperty = "connections";
    private const string ResourceServersProperty = "resourceServers";
    private const string ClientsProperty = "clients";
    private const string ClientGrantsProperty = "clientGrants";
    private const string RoleAssignmentsProperty = "roleAssignments";

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public string Name => SectionName;

    /// <inheritdoc />
    public int Order => TenantTransferOrders.Core;

    /// <inheritdoc />
    public async ValueTask<TenantTransferSection> ExportAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        using var scope = AmbientTenantAccessor.BeginScope(tenantId);

        var tenant =
            await storeManager
                .GetStore<ITenantStore>()
                .GetOrDefaultAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"The tenant '{tenantId}' does not exist.");

        var connections = await storeManager
            .GetStore<IConnectionStore>()
            .GetAllAsync(cancellationToken);

        var resourceServerStore = storeManager.GetStore<IResourceServerStore>();
        var resourceServers = await ReadAllAsync(
            (page, token) => resourceServerStore.GetPageAsync(page, token),
            cancellationToken
        );

        var clientStore = storeManager.GetStore<IClientStore>();
        var clients = await ReadAllAsync(
            (page, token) => clientStore.GetPageAsync(page, token),
            cancellationToken
        );

        var grantStore = storeManager.GetStore<IClientGrantStore>();
        var clientGrants = new List<PersistedClientGrant>();
        foreach (var client in clients)
        {
            var grantRows = await ReadAllAsync(
                (page, token) => grantStore.GetPageAsync(client.ClientId, page, token),
                cancellationToken
            );
            clientGrants.AddRange(grantRows);
        }

        var roleAssignments = await storeManager
            .GetStore<IRoleAssignmentStore>()
            .GetByResourceAsync(ResourceNodeTypes.Tenant, tenantId, cancellationToken);

        var document = new JsonObject
        {
            [TenantProperty] = JsonSerializer.SerializeToNode(tenant, JsonOptions),
            [ConnectionsProperty] = JsonSerializer.SerializeToNode(connections, JsonOptions),
            [ResourceServersProperty] = JsonSerializer.SerializeToNode(
                resourceServers,
                JsonOptions
            ),
            [ClientsProperty] = JsonSerializer.SerializeToNode(clients, JsonOptions),
            [ClientGrantsProperty] = JsonSerializer.SerializeToNode(clientGrants, JsonOptions),
            [RoleAssignmentsProperty] = JsonSerializer.SerializeToNode(
                roleAssignments,
                JsonOptions
            ),
        };

        return new TenantTransferSection
        {
            Name = Name,
            Json = JsonSerializer.SerializeToElement(document, JsonOptions),
        };
    }

    /// <inheritdoc />
    public async ValueTask<TenantTransferImportResult> ImportAsync(
        string tenantId,
        TenantTransferSection section,
        CancellationToken cancellationToken
    )
    {
        if (!string.Equals(section.Name, Name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The section '{section.Name}' is not the '{Name}' section.",
                nameof(section)
            );
        }

        var tenant = Read<PersistedTenant>(section.Json, TenantProperty);
        var connections = Read<List<PersistedConnection>>(section.Json, ConnectionsProperty);
        var resourceServers = Read<List<PersistedResourceServer>>(
            section.Json,
            ResourceServersProperty
        );
        var clients = Read<List<PersistedClient>>(section.Json, ClientsProperty);
        var clientGrants = Read<List<PersistedClientGrant>>(section.Json, ClientGrantsProperty);
        var roleAssignments = Read<List<PersistedRoleAssignment>>(
            section.Json,
            RoleAssignmentsProperty
        );

        EnsureTenant(tenantId, tenant.TenantId);
        EnsureTenant(tenantId, tenant.Settings.TenantId);
        EnsureTenant(tenantId, tenant.Secrets.TenantId);
        connections.ForEach(connection =>
        {
            EnsureTenant(tenantId, connection.TenantId);
            EnsureTenant(tenantId, connection.Settings.TenantId);
        });
        resourceServers.ForEach(resourceServer => EnsureTenant(tenantId, resourceServer.TenantId));
        clients.ForEach(client => EnsureTenant(tenantId, client.TenantId));
        clientGrants.ForEach(grant => EnsureTenant(tenantId, grant.TenantId));
        roleAssignments.ForEach(assignment => EnsureTenant(tenantId, assignment.TenantId));

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        using var scope = AmbientTenantAccessor.BeginScope(tenantId);

        // An imported tenant is fenced until the directory confirms the move; the import names the source cell.
        tenant.KeyCustody = TenantKeyCustody.Incoming;
        tenant.KeyCustodyCell = null;

        // A row refers to the rows it needs by a query, so each group is saved before the group that refers to it.
        await storeManager.GetStore<ITenantStore>().AddAsync(tenant, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        var connectionStore = storeManager.GetStore<IConnectionStore>();
        foreach (var connection in connections)
        {
            await connectionStore.AddAsync(connection, cancellationToken);
        }

        await storeManager.SaveChangesAsync(cancellationToken);

        var resourceServerStore = storeManager.GetStore<IResourceServerStore>();
        foreach (var resourceServer in resourceServers)
        {
            await resourceServerStore.AddAsync(resourceServer, cancellationToken);
        }

        await storeManager.SaveChangesAsync(cancellationToken);

        // The client store gives each client a principal of its own, so a role assignment follows its client's
        // principal.
        var principalIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var clientStore = storeManager.GetStore<IClientStore>();
        foreach (var client in clients)
        {
            var exportedPrincipalId = client.PrincipalId;
            await clientStore.AddAsync(client, cancellationToken);
            principalIds[exportedPrincipalId] = client.PrincipalId;
        }

        await storeManager.SaveChangesAsync(cancellationToken);

        var grantStore = storeManager.GetStore<IClientGrantStore>();
        foreach (var grant in clientGrants)
        {
            await grantStore.AddAsync(grant, cancellationToken);
        }

        var roleAssignmentStore = storeManager.GetStore<IRoleAssignmentStore>();
        foreach (var assignment in roleAssignments)
        {
            var principalId = principalIds.GetValueOrDefault(
                assignment.PrincipalId,
                assignment.PrincipalId
            );
            var roleAssignment = new PersistedRoleAssignment
            {
                TenantId = tenantId,
                AssignmentId = assignment.AssignmentId,
                PrincipalId = principalId,
                RoleName = assignment.RoleName,
                ResourceType = assignment.ResourceType,
                ResourceId = assignment.ResourceId,
                ConcurrencyToken = string.Empty,
            };
            await roleAssignmentStore.AddAsync(roleAssignment, cancellationToken);
        }

        await storeManager.SaveChangesAsync(cancellationToken);

        IEnumerable<PersistedSecret> secrets =
        [
            .. tenant.Secrets.Value,
            .. clients.SelectMany(client => client.Secrets.Value),
            .. resourceServers.SelectMany(resourceServer => resourceServer.Secrets.Value),
        ];

        return new TenantTransferImportResult { RemoteKeys = ReadRemoteKeys(secrets) };
    }

    /// <inheritdoc />
    // The rows of a tenant that has moved are removed with the tenant by ITenantStore.RemoveFromCellAsync.
    public ValueTask RemoveAsync(string tenantId, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private static async ValueTask<List<T>> ReadAllAsync<T>(
        Func<PageRequest, CancellationToken, ValueTask<PagedResult<T>>> readPage,
        CancellationToken cancellationToken
    )
    {
        var items = new List<T>();

        string? cursor = null;
        do
        {
            var pageRequest = new PageRequest { Cursor = cursor, Limit = PageLimit };
            var page = await readPage(pageRequest, cancellationToken);
            items.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        return items;
    }

    private static T Read<T>(JsonElement json, string property)
        where T : class
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(property, out var node))
        {
            throw new InvalidOperationException(
                $"The '{SectionName}' section has no '{property}' property."
            );
        }

        return node.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException(
                $"The '{property}' property of the '{SectionName}' section is null."
            );
    }

    private static void EnsureTenant(string tenantId, string recordTenantId)
    {
        if (!string.Equals(tenantId, recordTenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The '{SectionName}' section holds a record of tenant '{recordTenantId}', not of tenant '{tenantId}'."
            );
        }
    }

    private static List<RemoteKeyReference> ReadRemoteKeys(IEnumerable<PersistedSecret> secrets)
    {
        var remoteKeys = new List<RemoteKeyReference>();
        foreach (var secret in secrets)
        {
            if (
                SecretEncodings.IsRemote(secret.SecretEncoding)
                && RemoteKeyReference.TryParse(secret.EncodedValue, out var reference)
            )
            {
                remoteKeys.Add(reference);
            }
        }

        return [.. remoteKeys.Distinct()];
    }
}
