// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class PlatformTopologyTests : BaseTests
{
    private sealed class StubTopology : IPlatformTopology
    {
        public bool HasDirectory => false;
        public bool IsControlPlaneCell => false;
        public bool HasManagementTenant => false;
    }

    private static IConfiguration CreateConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))
            )
            .Build();

    private static DirectoryPlatformTopology CreateTopology(
        bool isControlPlane,
        string strategyCode
    )
    {
        var cellOptions = Options.Create(new CellOptions { IsControlPlane = isControlPlane });
        var tenantOptions = Options.Create(
            new TenantResolutionOptions { StrategyCode = strategyCode }
        );
        return new DirectoryPlatformTopology(cellOptions, tenantOptions);
    }

    [Fact]
    public void AddOpenIdDirectoryClient_NoUrl_RegistersNoTopology()
    {
        var serviceCollection = new ServiceCollection();
        var configuration = CreateConfiguration(("Cell:IsControlPlane", "false"));
        serviceCollection.NewBuilder<OpenIdClientLibrary>().AddOpenIdDirectoryClient(configuration);

        Assert.DoesNotContain(serviceCollection, x => x.ServiceType == typeof(IPlatformTopology));
    }

    [Fact]
    public void AddOpenIdDirectoryClient_Url_ReplacesTheLocalTopology()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IPlatformTopology, StubTopology>();
        serviceCollection.AddSingleton(Options.Create(new CellOptions()));
        serviceCollection.AddSingleton(Options.Create(new TenantResolutionOptions()));
        var configuration = CreateConfiguration(("Directory:Url", "https://directory.example"));
        serviceCollection.NewBuilder<OpenIdClientLibrary>().AddOpenIdDirectoryClient(configuration);

        using var provider = serviceCollection.BuildServiceProvider();
        var topology = provider.GetRequiredService<IPlatformTopology>();

        Assert.IsType<DirectoryPlatformTopology>(topology);
        Assert.True(topology.HasDirectory);
        Assert.Single(serviceCollection, x => x.ServiceType == typeof(IPlatformTopology));
    }

    [Theory]
    [InlineData(false, OpenIdConstants.TenantStrategyCodes.StaticSingle, false)]
    [InlineData(false, OpenIdConstants.TenantStrategyCodes.DynamicByHost, true)]
    [InlineData(false, OpenIdConstants.TenantStrategyCodes.DynamicByPath, true)]
    [InlineData(true, OpenIdConstants.TenantStrategyCodes.DynamicByHost, false)]
    [InlineData(true, OpenIdConstants.TenantStrategyCodes.StaticSingle, false)]
    public void HasManagementTenant_DependsOnStrategyAndControlPlane(
        bool isControlPlane,
        string strategyCode,
        bool expected
    )
    {
        var topology = CreateTopology(isControlPlane, strategyCode);

        Assert.True(topology.HasDirectory);
        Assert.Equal(isControlPlane, topology.IsControlPlaneCell);
        Assert.Equal(expected, topology.HasManagementTenant);
    }
}
