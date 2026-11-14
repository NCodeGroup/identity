// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.GlobalDirectory;

public sealed class LocalDirectoryRegistrationTests
{
    #region AddEntityFrameworkCorePersistence Tests

    [Fact]
    public void AddEntityFrameworkCorePersistence_WhenNoDirectoryIsRegistered_RegistersTheLocalAdaptersAsSingletons()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddEntityFrameworkCorePersistence<OpenIdDbContext>();

        var tenantDirectory = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ITenantDirectory)
        );
        Assert.Equal(typeof(LocalTenantDirectory), tenantDirectory.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, tenantDirectory.Lifetime);

        var teamDirectory = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ITeamDirectory)
        );
        Assert.Equal(typeof(LocalTeamDirectory), teamDirectory.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, teamDirectory.Lifetime);

        var cellDirectory = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ICellDirectory)
        );
        Assert.Equal(typeof(LocalCellDirectory), cellDirectory.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, cellDirectory.Lifetime);
    }

    [Fact]
    public void AddEntityFrameworkCorePersistence_WhenADirectoryIsRegisteredFirst_KeepsIt()
    {
        var tenantDirectory = Mock.Of<ITenantDirectory>();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(tenantDirectory);

        serviceCollection.AddEntityFrameworkCorePersistence<OpenIdDbContext>();

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ITenantDirectory)
        );
        Assert.Same(tenantDirectory, descriptor.ImplementationInstance);
    }

    #endregion
}
