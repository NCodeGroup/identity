// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        serviceCollection.AddRuntimeRolePostureCheck("Host=localhost", false);

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        Assert.Single(
            serviceProvider.GetServices<IHostedService>().OfType<RuntimeRolePostureCheck>()
        );
    }

    #endregion
}
