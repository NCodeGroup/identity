// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using IdGen;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Host;
using NCode.Identity.OpenId.Host.Database;
using NCode.Identity.OpenId.Persistence.EntityFramework;
using NCode.Identity.OpenId.Persistence.EntityFramework.Accounts;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Host;

/// <summary>
/// Provides unit tests for the <see cref="HostDatabaseRegistration"/> class.
/// </summary>
public class HostDatabaseRegistrationTests
{
    private const string ConnectionString =
        "Host=localhost;Database=openiddb;Username=identity_runtime";

    private static ServiceProvider CreateServiceProvider(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{HostDatabaseConnection.ConnectionStringName}"] =
                        connectionString,
                }
            )
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<IAmbientTenantAccessor, StubAmbientTenantAccessor>();
        serviceCollection.AddSingleton<IIdGenerator<long>>(new IdGenerator(0));
        serviceCollection
            .AddEntityFrameworkCorePersistence<OpenIdDbContext>()
            .AddLocalAccountEntityFramework();
        serviceCollection.AddHostDatabase(configuration);
        return serviceCollection.BuildServiceProvider();
    }

    #region AddHostDatabase Tests

    [Fact]
    public void AddHostDatabase_WhenConfigurationIsNull_Throws()
    {
        var serviceCollection = new ServiceCollection();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            serviceCollection.AddHostDatabase(null!)
        );

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public async Task AddHostDatabase_WhenAConnectionIsConfigured_CreatesNpgsqlOpenIdContextsThatCarryTheTenantScope()
    {
        await using var serviceProvider = CreateServiceProvider(ConnectionString);
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<OpenIdDbContext>>();

        await using var context = factory.CreateDbContext();

        var options = context.GetService<IDbContextOptions>();
        Assert.True(context.Database.IsNpgsql());
        Assert.Equal(
            ConnectionString,
            RelationalOptionsExtension.Extract(options).ConnectionString
        );
        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors;
        Assert.NotNull(interceptors);
        Assert.Single(interceptors.OfType<TenantSessionInterceptor>());
    }

    [Fact]
    public async Task AddHostDatabase_WhenAConnectionIsConfigured_KeepsTheKeyRingInTheHostDatabase()
    {
        await using var serviceProvider = CreateServiceProvider(ConnectionString);
        await using var scope = serviceProvider.CreateAsyncScope();

        var keyManagement = serviceProvider
            .GetRequiredService<IOptions<KeyManagementOptions>>()
            .Value;
        var dataProtection = serviceProvider
            .GetRequiredService<IOptions<DataProtectionOptions>>()
            .Value;
        var keyRingContext =
            scope.ServiceProvider.GetRequiredService<DeveloperDataProtectionDbContext>();

        Assert.IsType<EntityFrameworkCoreXmlRepository<DeveloperDataProtectionDbContext>>(
            keyManagement.XmlRepository
        );
        Assert.Equal("NCode.Identity.OpenId.Host", dataProtection.ApplicationDiscriminator);
        Assert.True(keyRingContext.Database.IsNpgsql());
    }

    [Fact]
    public async Task AddHostDatabase_WhenAConnectionIsConfigured_ChecksTheRuntimeRolePostureAtStart()
    {
        await using var serviceProvider = CreateServiceProvider(ConnectionString);

        var hostedServices = serviceProvider.GetServices<IHostedService>();

        Assert.Single(hostedServices.OfType<RuntimeRolePostureCheck>());
    }

    [Fact]
    public async Task AddHostDatabase_WhenAConnectionIsConfigured_ChecksTheDatabaseForReadiness()
    {
        await using var serviceProvider = CreateServiceProvider(ConnectionString);

        var healthChecks = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;

        var registration = Assert.Single(
            healthChecks.Registrations,
            x => x.Name == HostDatabaseRegistration.HealthCheckName
        );
        Assert.Contains(HealthCheckTags.Readiness, registration.Tags);
    }

    [Fact]
    public async Task AddHostDatabase_WhenNoConnectionIsConfigured_CreatesInMemoryOpenIdContexts()
    {
        await using var serviceProvider = CreateServiceProvider(null);
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<OpenIdDbContext>>();

        await using var context = factory.CreateDbContext();

        Assert.True(context.Database.IsInMemory());
    }

    [Fact]
    public async Task AddHostDatabase_WhenNoConnectionIsConfigured_RegistersNothingThatConnects()
    {
        await using var serviceProvider = CreateServiceProvider(null);

        var hostedServices = serviceProvider.GetServices<IHostedService>();
        var healthChecks = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;
        var keyRingOptions = serviceProvider.GetService<
            DbContextOptions<DeveloperDataProtectionDbContext>
        >();

        Assert.Empty(hostedServices.OfType<RuntimeRolePostureCheck>());
        Assert.DoesNotContain(
            healthChecks.Registrations,
            x => x.Name == HostDatabaseRegistration.HealthCheckName
        );
        Assert.Null(keyRingOptions);
    }

    #endregion

    private sealed class StubAmbientTenantAccessor : IAmbientTenantAccessor
    {
        public bool IsScoped => false;

        public string? TenantId => null;

        public bool IsUnscoped => false;

        public string? UnscopedReason => null;

        public IDisposable BeginScope(string tenantId) => throw new NotSupportedException();

        public IDisposable BeginUnscoped(string reason) => throw new NotSupportedException();
    }
}
