// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Integration.Tests.Infrastructure;

/// <summary>
/// An <see cref="IServiceScope"/> whose ambient tenant scope is set to one tenant for as long as the service scope
/// lives, so a test that seeds or reads tenant rows outside a request reaches the same rows a request to that tenant
/// does.
/// </summary>
internal sealed class TenantServiceScope : IServiceScope
{
    private IServiceScope InnerScope { get; }

    private IDisposable TenantScope { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantServiceScope"/> class.
    /// </summary>
    /// <param name="services">The root service provider to create the service scope from.</param>
    /// <param name="tenantId">The identifier of the tenant to scope tenant-bound data access to.</param>
    public TenantServiceScope(IServiceProvider services, string tenantId)
    {
        InnerScope = services.CreateScope();
        TenantScope = InnerScope
            .ServiceProvider.GetRequiredService<IAmbientTenantAccessor>()
            .BeginScope(tenantId);
    }

    /// <inheritdoc />
    public IServiceProvider ServiceProvider => InnerScope.ServiceProvider;

    /// <inheritdoc />
    public void Dispose()
    {
        TenantScope.Dispose();
        InnerScope.Dispose();
    }
}
