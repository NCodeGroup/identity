// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management.Transfers;

/// <summary>
/// Provides an implementation of <see cref="ITenantTransferContributor"/> that carries a tenant's local accounts as the
/// records of <see cref="ILocalAccountTransferStore"/>, with their password hashes, security stamps and claims. The
/// section is sensitive and is handled only inside an encrypted transfer. A local account references no remote key.
/// </summary>
// See ADR-1514.
internal class LocalAccountTenantTransferContributor(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor
) : ITenantTransferContributor
{
    /// <summary>
    /// The name of the section that the contributor writes.
    /// </summary>
    internal const string SectionName = "local-accounts";

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

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

        var records = await storeManager
            .GetStore<ILocalAccountTransferStore>()
            .ExportAsync(cancellationToken);

        return new TenantTransferSection
        {
            Name = Name,
            Json = JsonSerializer.SerializeToElement(records, JsonOptions),
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

        if (section.Json.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"The '{SectionName}' section does not hold an array of records."
            );
        }

        var records =
            section.Json.Deserialize<List<LocalAccountTransferRecord>>(JsonOptions)
            ?? throw new InvalidOperationException($"The '{SectionName}' section is null.");

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        using var scope = AmbientTenantAccessor.BeginScope(tenantId);

        await storeManager
            .GetStore<ILocalAccountTransferStore>()
            .ImportAsync(records, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        return new TenantTransferImportResult { RemoteKeys = [] };
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        using var scope = AmbientTenantAccessor.BeginScope(tenantId);

        await storeManager.GetStore<ILocalAccountTransferStore>().RemoveAllAsync(cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);
    }
}
