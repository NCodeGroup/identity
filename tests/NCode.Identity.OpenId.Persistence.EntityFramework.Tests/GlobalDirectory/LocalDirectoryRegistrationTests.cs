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
    public void AddEntityFrameworkCorePersistence_WhenNoDirectoryIsRegistered_RegistersTheLocalAdapters()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddEntityFrameworkCorePersistence<OpenIdDbContext>();

        Assert.Equal(
            typeof(LocalTenantDirectory),
            Assert
                .Single(serviceCollection, x => x.ServiceType == typeof(ITenantDirectory))
                .ImplementationType
        );
        Assert.Equal(
            typeof(LocalTeamDirectory),
            Assert
                .Single(serviceCollection, x => x.ServiceType == typeof(ITeamDirectory))
                .ImplementationType
        );
        Assert.Equal(
            typeof(LocalCellDirectory),
            Assert
                .Single(serviceCollection, x => x.ServiceType == typeof(ICellDirectory))
                .ImplementationType
        );
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
