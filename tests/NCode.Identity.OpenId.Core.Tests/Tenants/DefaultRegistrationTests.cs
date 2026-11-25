// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.OpenId.Tenants.Keys;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Tenants;

public sealed class DefaultRegistrationTests : BaseTests
{
    private static IServiceBuilder<OpenIdCoreLibrary> CreateBuilder(
        IServiceCollection serviceCollection
    ) => new ServiceBuilder<OpenIdCoreLibrary>(serviceCollection);

    #region AddTenantResolutionServices Tests

    [Fact]
    public void AddTenantResolutionServices_Always_RegistersTheManagementKeyRevokerAsScoped()
    {
        var serviceCollection = new ServiceCollection();

        CreateBuilder(serviceCollection).AddTenantResolutionServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(IManagementKeyRevoker)
        );
        Assert.Equal(typeof(DefaultManagementKeyRevoker), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddTenantResolutionServices_Always_RegistersTheCustodyGuardAsASingleton()
    {
        var serviceCollection = new ServiceCollection();

        CreateBuilder(serviceCollection).AddTenantResolutionServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ITenantKeyCustodyGuard)
        );
        Assert.Equal(typeof(DefaultTenantKeyCustodyGuard), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddTenantResolutionServices_Always_RegistersTheCustodyReconcilerAsAHostedService()
    {
        var serviceCollection = new ServiceCollection();

        CreateBuilder(serviceCollection).AddTenantResolutionServices();

        var descriptor = Assert.Single(
            serviceCollection,
            x =>
                x.ServiceType == typeof(IHostedService)
                && x.ImplementationType == typeof(TenantKeyCustodyReconciler)
        );
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    #endregion
}
