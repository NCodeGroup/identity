// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Accounts.Management.Transfers;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Endpoints;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.Registration;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Accounts.Management;

public sealed class DefaultRegistrationTests
{
    #region AddLocalAccountManagement Tests

    [Fact]
    public void AddLocalAccountManagement_Always_RegistersTheLocalAccountTransferContributorAsASingleton()
    {
        var serviceCollection = new ServiceCollection();
        var builder = new ServiceBuilder<OpenIdManagementLibrary>(serviceCollection);
        var localAccountStore = new ServiceBuilder<LocalAccountLibrary>(serviceCollection);

        builder.AddLocalAccountManagement(localAccountStore);

        var descriptor = Assert.Single(
            serviceCollection,
            x => x.ServiceType == typeof(ITenantTransferContributor)
        );
        Assert.Equal(typeof(LocalAccountTenantTransferContributor), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddLocalAccountManagement_Always_RegistersThePlatformEndpointProviderUnderTheRootGroup()
    {
        var serviceCollection = new ServiceCollection();
        var builder = new ServiceBuilder<OpenIdManagementLibrary>(serviceCollection);
        var localAccountStore = new ServiceBuilder<LocalAccountLibrary>(serviceCollection);

        builder.AddLocalAccountManagement(localAccountStore);

        var descriptor = Assert.Single(
            serviceCollection,
            x =>
                x.ServiceType == typeof(IEndpointProvider)
                && x.IsKeyedService
                && Equals(x.ServiceKey, ManagementEndpointGroups.Root)
                && x.KeyedImplementationType == typeof(PlatformLocalAccountEndpointProvider)
        );
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    #endregion
}
