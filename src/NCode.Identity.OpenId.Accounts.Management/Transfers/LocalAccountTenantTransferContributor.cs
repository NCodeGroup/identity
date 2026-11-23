// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management.Transfers;

/// <summary>
/// Provides an implementation of <see cref="ITenantTransferContributor"/> that carries a tenant's local accounts as the
/// records of each local connection's <see cref="ILocalAccountTransferStore"/>, grouped by connection id, with their
/// password hashes, security stamps and claims. The section is sensitive and is handled only inside an encrypted
/// transfer. A local account references no remote key.
/// </summary>
// See ADR-1514.
internal class LocalAccountTenantTransferContributor(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    LocalAccountStoreCatalog localAccountStoreCatalog
) : ITenantTransferContributor
{
    /// <summary>
    /// The name of the section that the contributor writes.
    /// </summary>
    internal const string SectionName = "local-accounts";

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    private LocalAccountStoreCatalog LocalAccountStoreCatalog { get; } = localAccountStoreCatalog;

    /// <inheritdoc />
    public string Name => SectionName;

    /// <inheritdoc />
    public int Order => TenantTransferOrders.LocalAccounts;

    /// <inheritdoc />
    public async ValueTask<TenantTransferSection> ExportAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        using var scope = AmbientTenantAccessor.BeginScope(tenantId);

        var connections = await GetLocalConnectionsAsync(storeManager, cancellationToken);
        var recordsByConnection = new Dictionary<string, IReadOnlyList<LocalAccountTransferRecord>>(
            StringComparer.Ordinal
        );
        foreach (var connection in connections)
        {
            var transferStore = LocalAccountStoreCatalog.GetTransferStore(storeManager, connection);
            var records = await transferStore.ExportAsync(
                connection.ConnectionId,
                cancellationToken
            );
            recordsByConnection[connection.ConnectionId] = records;
        }

        return new TenantTransferSection
        {
            Name = Name,
            Json = JsonSerializer.SerializeToElement(recordsByConnection, JsonOptions),
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

        if (section.Json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"The '{SectionName}' section does not hold an object of records by connection."
            );
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        using var scope = AmbientTenantAccessor.BeginScope(tenantId);

        var connectionStore = storeManager.GetStore<IConnectionStore>();
        foreach (var property in section.Json.EnumerateObject())
        {
            var records =
                property.Value.Deserialize<List<LocalAccountTransferRecord>>(JsonOptions)
                ?? throw new InvalidOperationException(
                    $"The '{SectionName}' section holds no records for the connection '{property.Name}'."
                );

            var connection =
                await connectionStore.GetOrDefaultAsync(property.Name, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"The '{SectionName}' section names the connection '{property.Name}', which the tenant does not have."
                );

            var transferStore = LocalAccountStoreCatalog.GetTransferStore(storeManager, connection);
            await transferStore.ImportAsync(connection.ConnectionId, records, cancellationToken);
        }

        await storeManager.SaveChangesAsync(cancellationToken);

        return new TenantTransferImportResult { RemoteKeys = [] };
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        using var scope = AmbientTenantAccessor.BeginScope(tenantId);

        var connections = await GetLocalConnectionsAsync(storeManager, cancellationToken);
        foreach (var connection in connections)
        {
            var transferStore = LocalAccountStoreCatalog.GetTransferStore(storeManager, connection);
            await transferStore.RemoveAllAsync(connection.ConnectionId, cancellationToken);
        }

        await storeManager.SaveChangesAsync(cancellationToken);
    }

    private static async ValueTask<List<PersistedConnection>> GetLocalConnectionsAsync(
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var all = await storeManager.GetStore<IConnectionStore>().GetAllAsync(cancellationToken);
        return all.Where(IsLocal).ToList();
    }

    private static bool IsLocal(PersistedConnection connection) =>
        string.Equals(connection.Strategy, ConnectionStrategyNames.Local, StringComparison.Ordinal);
}
