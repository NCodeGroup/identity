// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class ControlPlaneAdapterRegistrationTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IManagementTokenValidator> MockRemoteValidator { get; }

    private Mock<IControlPlaneAuthority> MockRemoteAuthority { get; }

    private Mock<IControlPlanePrincipalDirectory> MockRemoteDirectory { get; }

    public ControlPlaneAdapterRegistrationTests()
    {
        MockRemoteValidator = MockRepository.Create<IManagementTokenValidator>();
        MockRemoteAuthority = MockRepository.Create<IControlPlaneAuthority>();
        MockRemoteDirectory = MockRepository.Create<IControlPlanePrincipalDirectory>();
    }

    private ServiceProvider BuildServiceProvider(bool isControlPlane, bool registerRemote)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.NewBuilder<OpenIdCoreLibrary>().AddOpenIdManagementLibrary();

        serviceCollection.AddSingleton(
            Options.Create(new CellOptions { IsControlPlane = isControlPlane })
        );
        serviceCollection.AddSingleton(MockRepository.Create<IClientPrincipalResolver>().Object);
        serviceCollection.AddSingleton(MockRepository.Create<IStoreManagerFactory>().Object);
        serviceCollection.AddSingleton(MockRepository.Create<IAmbientTenantAccessor>().Object);
        serviceCollection.AddSingleton(MockRepository.Create<ITenantResolver>().Object);

        if (registerRemote)
        {
            serviceCollection.AddKeyedSingleton(
                ControlPlaneServiceKeys.Remote,
                MockRemoteValidator.Object
            );
            serviceCollection.AddKeyedSingleton(
                ControlPlaneServiceKeys.Remote,
                MockRemoteAuthority.Object
            );
            serviceCollection.AddKeyedSingleton(
                ControlPlaneServiceKeys.Remote,
                MockRemoteDirectory.Object
            );
        }

        return serviceCollection.BuildServiceProvider();
    }

    #region Resolve Tests

    [Fact]
    public void Resolve_ControlPlaneCellWithKeyedRemote_ReturnsLocalAdapters()
    {
        using var serviceProvider = BuildServiceProvider(
            isControlPlane: true,
            registerRemote: true
        );

        Assert.IsType<LocalManagementTokenValidator>(
            serviceProvider.GetRequiredService<IManagementTokenValidator>()
        );
        Assert.IsType<LocalControlPlaneAuthority>(
            serviceProvider.GetRequiredService<IControlPlaneAuthority>()
        );
        var directory = serviceProvider.GetRequiredService<IControlPlanePrincipalDirectory>();
        Assert.IsType<LocalControlPlanePrincipalDirectory>(directory);
    }

    [Fact]
    public void Resolve_WorkloadCellWithKeyedRemote_ReturnsRemoteAdapters()
    {
        using var serviceProvider = BuildServiceProvider(
            isControlPlane: false,
            registerRemote: true
        );

        Assert.Same(
            MockRemoteValidator.Object,
            serviceProvider.GetRequiredService<IManagementTokenValidator>()
        );
        Assert.Same(
            MockRemoteAuthority.Object,
            serviceProvider.GetRequiredService<IControlPlaneAuthority>()
        );
        var directory = serviceProvider.GetRequiredService<IControlPlanePrincipalDirectory>();
        Assert.Same(MockRemoteDirectory.Object, directory);
    }

    [Fact]
    public void Resolve_WorkloadCellWithoutRemote_ReturnsLocalAdapters()
    {
        using var serviceProvider = BuildServiceProvider(
            isControlPlane: false,
            registerRemote: false
        );

        Assert.IsType<LocalManagementTokenValidator>(
            serviceProvider.GetRequiredService<IManagementTokenValidator>()
        );
        Assert.IsType<LocalControlPlaneAuthority>(
            serviceProvider.GetRequiredService<IControlPlaneAuthority>()
        );
        var directory = serviceProvider.GetRequiredService<IControlPlanePrincipalDirectory>();
        Assert.IsType<LocalControlPlanePrincipalDirectory>(directory);
    }

    #endregion
}
