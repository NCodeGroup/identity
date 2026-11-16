// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory.Server;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Tests;

public sealed class DirectoryDatabaseRegistrationTests
{
    private const string ConnectionString = "Host=localhost;Database=directory";

    #region AddDirectoryDatabase Tests

    [Fact]
    public void AddDirectoryDatabase_NullServiceCollection_Throws()
    {
        IServiceCollection serviceCollection = null!;

        var exception = Assert.Throws<ArgumentNullException>(() =>
            serviceCollection.AddDirectoryDatabase(ConnectionString)
        );

        Assert.Equal("serviceCollection", exception.ParamName);
    }

    [Fact]
    public async Task AddDirectoryDatabase_ConnectionString_RegistersTheContextFactory()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddDirectoryDatabase(ConnectionString);

        await using var provider = serviceCollection.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IDbContextFactory<DirectoryDbContext>>());
    }

    [Fact]
    public async Task AddDirectoryDatabase_ConnectionString_RegistersTheReadinessCheck()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddDirectoryDatabase(ConnectionString);

        await using var provider = serviceCollection.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>();
        var registration = Assert.Single(
            options.Value.Registrations,
            candidate => candidate.Name == DirectoryDatabaseRegistration.HealthCheckName
        );
        Assert.Contains(HealthCheckTags.Readiness, registration.Tags);
    }

    #endregion
}
