// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Secrets.RemoteKeys;

namespace NCode.Identity.OpenId.Integration.Tests.Infrastructure;

/// <summary>
/// A <see cref="BootstrapAdminApplicationFactory"/> whose host stands for one cell of a region. Every factory owns
/// its database, key ring and root key, so two factories are two cells; the cells of a test share one
/// <see cref="TestRemoteKeyProvider"/>, which stands for the cloud key store, and one
/// <see cref="InMemoryTenantDirectory"/>, which stands for the global directory. Each host resolves tenants by host
/// name beneath the root issuer <c>http://localhost</c>, and provisions the keys of a tenant in the provider.
/// </summary>
public sealed class CellApplicationFactory(
    TestRemoteKeyProvider provider,
    InMemoryTenantDirectory directory,
    string cellName,
    string region
)
    : BootstrapAdminApplicationFactory(
        new Dictionary<string, string?>
        {
            ["Cell:Name"] = cellName,
            ["Cell:Region"] = region,
            ["OpenId:TenantResolution:StrategyCode"] = OpenIdConstants
                .TenantStrategyCodes
                .DynamicByHost,
            ["OpenId:TenantResolution:Root:Issuer"] = "http://localhost",
            ["OpenId:TenantResolution:DynamicByHost:IssuerTemplate"] = IssuerTemplate,
            [$"OpenId:Platform:Settings:{OpenIdSettingNames.TenantKeyProvider}"] =
                TestRemoteKeyProvider.ProviderName,
            ["SecretProtection:RemoteKeys:SweepEnabled"] = "false",
        },
        RootTenantId
    )
{
    /// <summary>
    /// The issuer of a tenant, with the tenant's identifier in place of <c>{tenantId}</c>.
    /// </summary>
    public const string IssuerTemplate = "https://{tenantId}.auth.test";

    /// <summary>
    /// The identifier of the tenant that the bootstrap administrator belongs to.
    /// </summary>
    public const string RootTenantId = "root";

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(serviceCollection =>
        {
            serviceCollection.RemoveAll<ITenantDirectory>();
            serviceCollection.AddSingleton<ITenantDirectory>(directory);
            serviceCollection.AddSingleton<IRemoteKeyProvider>(provider);
            serviceCollection.AddSingleton<IRemoteKeyDescriptorSource>(provider);
        });
    }
}
