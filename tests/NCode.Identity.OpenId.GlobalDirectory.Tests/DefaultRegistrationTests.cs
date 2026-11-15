// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.GlobalDirectory.ControlPlane;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DefaultRegistrationTests : BaseTests
{
    private static IConfiguration CreateConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))
            )
            .Build();

    private static List<ServiceDescriptor> Register(IConfiguration configuration)
    {
        var serviceCollection = new ServiceCollection();
        var builder = serviceCollection.NewBuilder<OpenIdClientLibrary>();
        var count = serviceCollection.Count;
        builder.AddOpenIdDirectoryClient(configuration);
        return [.. serviceCollection.Skip(count)];
    }

    private static bool IsRegistered<TService, TImplementation>(
        IEnumerable<ServiceDescriptor> serviceCollection
    ) =>
        serviceCollection.Any(x =>
            x.ServiceType == typeof(TService) && x.ImplementationType == typeof(TImplementation)
        );

    #region AddOpenIdDirectoryClient Tests

    [Fact]
    public void AddOpenIdDirectoryClient_NoUrl_RegistersNoDirectoryServices()
    {
        var serviceCollection = Register(
            CreateConfiguration(("Cell:IsControlPlane", "false"), ("Directory:ClientId", "cell-a"))
        );

        Assert.Empty(serviceCollection);
    }

    [Fact]
    public void AddOpenIdDirectoryClient_UrlOnControlPlane_RegistersNoRemoteManagementTokenValidator()
    {
        var serviceCollection = Register(
            CreateConfiguration(
                ("Directory:Url", "https://directory.example"),
                ("Cell:IsControlPlane", "true")
            )
        );

        Assert.True(IsRegistered<ITenantDirectory, RemoteTenantDirectory>(serviceCollection));
        Assert.DoesNotContain(
            serviceCollection,
            x => x.ServiceType == typeof(IManagementTokenValidator)
        );
        Assert.DoesNotContain(
            serviceCollection,
            x => x.ServiceType == typeof(IControlPlaneAuthority)
        );
    }

    [Fact]
    public void AddOpenIdDirectoryClient_UrlOnCell_RegistersRemoteManagementTokenValidator()
    {
        var serviceCollection = Register(
            CreateConfiguration(
                ("Directory:Url", "https://directory.example"),
                ("Cell:IsControlPlane", "false")
            )
        );

        Assert.True(IsRegistered<ITenantDirectory, RemoteTenantDirectory>(serviceCollection));
        Assert.True(
            IsRegistered<IManagementTokenValidator, RemoteManagementTokenValidator>(
                serviceCollection
            )
        );
        Assert.True(
            IsRegistered<IControlPlaneAuthority, RemoteControlPlaneAuthority>(serviceCollection)
        );
    }

    [Fact]
    public void AddOpenIdDirectoryClient_UnknownDirectoryKey_Throws()
    {
        var configuration = CreateConfiguration(
            ("Directory:Url", "https://directory.example"),
            ("Directory:Unknown", "value")
        );

        var exception = Assert.ThrowsAny<Exception>(() => Register(configuration));

        Assert.Contains("Unknown", exception.Message);
    }

    #endregion
}
