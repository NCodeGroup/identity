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

    private static bool IsRegisteredKeyed<TService, TImplementation>(
        IEnumerable<ServiceDescriptor> serviceCollection,
        object key
    ) =>
        serviceCollection.Any(x =>
            x.ServiceType == typeof(TService)
            && x.IsKeyedService
            && Equals(x.ServiceKey, key)
            && x.KeyedImplementationType == typeof(TImplementation)
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

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void AddOpenIdDirectoryClient_Url_RegistersKeyedRemoteControlPlaneAdapters(
        string isControlPlane
    )
    {
        var serviceCollection = Register(
            CreateConfiguration(
                ("Directory:Url", "https://directory.example"),
                ("Cell:IsControlPlane", isControlPlane)
            )
        );

        Assert.True(IsRegistered<ITenantDirectory, RemoteTenantDirectory>(serviceCollection));
        Assert.True(
            IsRegisteredKeyed<IManagementTokenValidator, RemoteManagementTokenValidator>(
                serviceCollection,
                ControlPlaneServiceKeys.Remote
            )
        );
        Assert.True(
            IsRegisteredKeyed<IControlPlaneAuthority, RemoteControlPlaneAuthority>(
                serviceCollection,
                ControlPlaneServiceKeys.Remote
            )
        );
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void AddOpenIdDirectoryClient_Url_RegistersNoUnkeyedControlPlaneAdapters(
        string isControlPlane
    )
    {
        var serviceCollection = Register(
            CreateConfiguration(
                ("Directory:Url", "https://directory.example"),
                ("Cell:IsControlPlane", isControlPlane)
            )
        );

        Assert.DoesNotContain(
            serviceCollection,
            x => x.ServiceType == typeof(IManagementTokenValidator) && !x.IsKeyedService
        );
        Assert.DoesNotContain(
            serviceCollection,
            x => x.ServiceType == typeof(IControlPlaneAuthority) && !x.IsKeyedService
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
