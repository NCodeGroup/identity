// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace NCode.Identity.AspNetCore.Tests;

public sealed class HostFilteringRegistrationTests
{
    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection([]).Build();

    private static ServiceCollection CreateServiceCollection(params string[] allowedHosts)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        serviceCollection.Configure<HostFilteringOptions>(options =>
            options.AllowedHosts = [.. allowedHosts]
        );
        return serviceCollection;
    }

    #region AddHostFilteringValidation Tests

    [Fact]
    public void AddHostFilteringValidation_CalledTwice_RegistersOneSingletonValidator()
    {
        var serviceCollection = CreateServiceCollection("id.example.com");
        var configuration = CreateConfiguration();

        serviceCollection.AddHostFilteringValidation(configuration);
        serviceCollection.AddHostFilteringValidation(configuration);

        var descriptor = Assert.Single(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(IValidateOptions<HostFilteringOptions>)
        );
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddHostFilteringValidation_WildcardOutsideLocalDevelopment_ThrowsOnRead()
    {
        var serviceCollection = CreateServiceCollection("*");
        serviceCollection.AddHostFilteringValidation(CreateConfiguration());

        using var serviceProvider = serviceCollection.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            serviceProvider.GetRequiredService<IOptions<HostFilteringOptions>>().Value
        );
    }

    [Fact]
    public void AddHostFilteringValidation_NamedHost_ReadsCleanly()
    {
        var serviceCollection = CreateServiceCollection("id.example.com");
        serviceCollection.AddHostFilteringValidation(CreateConfiguration());

        using var serviceProvider = serviceCollection.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<HostFilteringOptions>>().Value;
        Assert.Equal(["id.example.com"], options.AllowedHosts);
    }

    #endregion

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = string.Empty;

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
