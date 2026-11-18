// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NCode.Identity.AspNetCore;
using Xunit;

namespace NCode.Identity.OpenId.Host.Database.Tests;

public sealed class RuntimeRolePostureCheckRegistrationTests
{
    #region AddRuntimeRolePostureCheck Tests

    [Fact]
    public async Task AddRuntimeRolePostureCheck_WhenCalled_RegistersTheCheckAsAHostedService()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();

        serviceCollection.AddRuntimeRolePostureCheck("Host=localhost");

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        Assert.Single(
            serviceProvider.GetServices<IHostedService>().OfType<RuntimeRolePostureCheck>()
        );
    }

    [Fact]
    public async Task AddRuntimeRolePostureCheck_WhenNoModeIsRegistered_RegistersTheDeploymentMode()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.AddRuntimeRolePostureCheck("Host=localhost");

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        var mode = serviceProvider.GetRequiredService<LocalDevelopmentMode>();
        Assert.False(mode.Enabled);
    }

    [Fact]
    public async Task AddRuntimeRolePostureCheck_WhenAModeIsRegistered_KeepsIt()
    {
        var registeredMode = new LocalDevelopmentMode(true);
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(registeredMode);

        serviceCollection.AddRuntimeRolePostureCheck("Host=localhost");

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        Assert.Same(registeredMode, serviceProvider.GetRequiredService<LocalDevelopmentMode>());
    }

    #endregion
}
