// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Resolves the <see cref="IConnectionStrategyCatalog"/> as the host starts, so a connection strategy with a blank
/// name, or two strategies of one name, stops the host rather than the first request that resolves the catalog.
/// </summary>
internal sealed class DefaultConnectionStrategyCatalogWarmup(IServiceProvider serviceProvider)
    : IHostedService
{
    private IServiceProvider ServiceProvider { get; } = serviceProvider;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">A registered strategy has a blank name, or two strategies have the
    /// same name.</exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ServiceProvider.GetRequiredService<IConnectionStrategyCatalog>();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
