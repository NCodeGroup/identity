// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NCode.Identity.OpenId.Connections;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Connections;

public class DefaultConnectionStrategyCatalogTests
{
    private sealed class TestStrategy(string name) : IConnectionStrategy
    {
        public string Name { get; } = name;

        public string SignInMethod => ConnectionSignInMethods.Password;

        public string? ValidateOptions(JsonElement options) => null;

        public string? ValidateOptionsChange(JsonElement current, JsonElement proposed) => null;
    }

    [Fact]
    public void TryGetStrategy_WhenRegistered_ReturnsTheStrategy()
    {
        var strategy = new TestStrategy("local");
        var catalog = new DefaultConnectionStrategyCatalog([strategy]);

        var found = catalog.TryGetStrategy("local", out var result);

        Assert.True(found);
        Assert.Same(strategy, result);
    }

    [Fact]
    public void TryGetStrategy_WhenNotRegisteredOrEmpty_ReturnsFalse()
    {
        var catalog = new DefaultConnectionStrategyCatalog([new TestStrategy("local")]);

        Assert.False(catalog.TryGetStrategy("other", out _));
        Assert.False(catalog.TryGetStrategy("", out _));
    }

    [Fact]
    public void Strategies_ListsEveryRegisteredStrategy()
    {
        var first = new TestStrategy("local");
        var second = new TestStrategy("corp");
        var catalog = new DefaultConnectionStrategyCatalog([first, second]);

        Assert.Equal(2, catalog.Strategies.Count);
        Assert.Contains(first, catalog.Strategies);
        Assert.Contains(second, catalog.Strategies);
    }

    [Fact]
    public void Constructor_WhenNameIsRegisteredTwice_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DefaultConnectionStrategyCatalog([
                new TestStrategy("local"),
                new TestStrategy("local"),
            ])
        );

        Assert.Contains("'local'", exception.Message);
    }

    [Fact]
    public void Constructor_WhenStrategyHasNoName_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new DefaultConnectionStrategyCatalog([new TestStrategy("")])
        );
    }

    [Fact]
    public void Constructor_WhenStrategyNameIsWhitespace_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new DefaultConnectionStrategyCatalog([new TestStrategy("  ")])
        );
    }

    [Fact]
    public async Task StartAsync_WhenNameIsRegisteredTwice_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionStrategy>(new TestStrategy("local"));
        services.AddSingleton<IConnectionStrategy>(new TestStrategy("local"));
        services.AddSingleton<IConnectionStrategyCatalog, DefaultConnectionStrategyCatalog>();
        services.AddSingleton<IHostedService, DefaultConnectionStrategyCatalogWarmup>();
        await using var provider = services.BuildServiceProvider();

        var warmups = provider.GetServices<IHostedService>();

        foreach (var warmup in warmups)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                warmup.StartAsync(CancellationToken.None)
            );
        }
    }
}
