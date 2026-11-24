// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Resolves the <see cref="LocalAccountStoreCatalog"/> as the host starts, so a local-account store with an empty name,
/// or two stores of one name, stops the host rather than the first request that resolves the catalog.
/// </summary>
internal sealed class LocalAccountStoreCatalogWarmup(IServiceProvider serviceProvider)
    : IHostedService
{
    private IServiceProvider ServiceProvider { get; } = serviceProvider;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">A registered store has an empty name, or two stores have the same
    /// name.</exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ServiceProvider.GetRequiredService<LocalAccountStoreCatalog>();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
