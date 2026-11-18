// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Registration;
using NCode.Identity.Server.DevelopmentEnvironment;
using Xunit;

namespace NCode.Identity.Server.Tests;

public sealed class DevelopmentEnvironmentRegistrationTests
{
    private const string ControlPlaneTenantId = "control";

    private static IConfiguration CreateConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))
            )
            .Build();

    private static ServiceCollection AddDevelopmentEnvironment(IConfiguration configuration)
    {
        var serviceCollection = new ServiceCollection();
        new ServiceBuilder<IdentityServer>(serviceCollection).AddDevelopmentEnvironment(
            configuration
        );

        // The bootstrap administrator's tenant is validated against the control-plane tenant.
        var tenantResolver = new Mock<ITenantResolver>(MockBehavior.Strict);
        tenantResolver.Setup(x => x.GetControlPlaneTenantId()).Returns(ControlPlaneTenantId);
        serviceCollection.AddSingleton(tenantResolver.Object);

        return serviceCollection;
    }

    #region AddDevelopmentEnvironment Tests

    [Fact]
    public void AddDevelopmentEnvironment_WhenNothingIsConfigured_RegistersNoConvenience()
    {
        var serviceCollection = AddDevelopmentEnvironment(CreateConfiguration());

        Assert.DoesNotContain(
            serviceCollection,
            descriptor => descriptor.ImplementationType == typeof(DeveloperSecretPruningSeedHandler)
        );
        Assert.DoesNotContain(
            serviceCollection,
            descriptor => descriptor.ImplementationType == typeof(DeveloperPlatformMasterKeyPruning)
        );
        Assert.DoesNotContain(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(ICorsService)
        );
    }

    [Fact]
    public void AddDevelopmentEnvironment_WhenPruneUnreadableKeysIsTrue_RegistersThePruner()
    {
        var serviceCollection = AddDevelopmentEnvironment(
            CreateConfiguration(("DeveloperKeys:PruneUnreadableKeys", "true"))
        );

        Assert.Contains(
            serviceCollection,
            descriptor => descriptor.ImplementationType == typeof(DeveloperSecretPruningSeedHandler)
        );
        Assert.Contains(
            serviceCollection,
            descriptor => descriptor.ImplementationType == typeof(DeveloperPlatformMasterKeyPruning)
        );
    }

    [Fact]
    public void AddDevelopmentEnvironment_WhenCorsOriginsAreConfigured_RegistersTheCorsPolicy()
    {
        var serviceCollection = AddDevelopmentEnvironment(
            CreateConfiguration(("Cors:AllowedOrigins:0", "https://localhost:5173"))
        );

        Assert.Contains(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(ICorsService)
        );
    }

    [Fact]
    public void AddDevelopmentEnvironment_WhenBootstrapAdminConfigured_BindsIt()
    {
        var serviceCollection = AddDevelopmentEnvironment(
            CreateConfiguration(
                ("BootstrapAdmin:ClientId", "bootstrap"),
                ("BootstrapAdmin:ClientSecret", "c2VjcmV0"),
                ("BootstrapAdmin:TenantId", ControlPlaneTenantId)
            )
        );
        using var serviceProvider = serviceCollection.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<BootstrapAdminOptions>>().Value;

        Assert.Equal("bootstrap", options.ClientId);
        Assert.True(options.IsEnabled);
        Assert.Contains(
            serviceCollection,
            descriptor => descriptor.ImplementationType == typeof(BootstrapAdminSeedHandler)
        );
    }

    [Fact]
    public void AddDevelopmentEnvironment_WhenBootstrapAdminHasUnknownKey_Throws()
    {
        var serviceCollection = AddDevelopmentEnvironment(
            CreateConfiguration(("BootstrapAdmin:Secret", "c2VjcmV0"))
        );
        using var serviceProvider = serviceCollection.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceProvider.GetRequiredService<IOptions<BootstrapAdminOptions>>().Value
        );

        Assert.Contains("'BootstrapAdmin'", exception.Message, StringComparison.Ordinal);
    }

    #endregion
}
