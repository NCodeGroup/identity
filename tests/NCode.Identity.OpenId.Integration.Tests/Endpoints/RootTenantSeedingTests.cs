// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

public class RootTenantSeedingTests(HostApplicationFactory factory)
    : IClassFixture<HostApplicationFactory>
{
    private HostApplicationFactory Factory { get; } = factory;

    // The static single-tenant strategy serves only its own tenant, which is the root tenant, so the Development host
    // (the control-plane cell) holds the control plane's resource servers in the served tenant (ADR-1411).
    [Fact]
    public async Task ResolvingTheStaticTenant_SeedsTheDirectoryResourceServerIntoIt()
    {
        using var warmupClient = Factory.CreateClient();
        using var warmupResponse = await warmupClient.GetAsync("/oauth2/jwks");

        using var scope = new TenantServiceScope(Factory.Services, HostApplicationFactory.TenantId);
        var storeManagerFactory = scope.ServiceProvider.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(
            CancellationToken.None
        );
        var store = storeManager.GetStore<IResourceServerStore>();

        var directoryResourceServer = await store.GetByIdentifierOrDefaultAsync(
            GlobalDirectoryProtocol.DirectoryAudience,
            CancellationToken.None
        );

        Assert.NotNull(directoryResourceServer);
    }

    // The static single-tenant strategy serves only its own tenant, which is the root tenant, so no separate root
    // tenant is created beside it.
    [Fact]
    public async Task ResolvingTheStaticTenant_CreatesNoOtherRootTenant()
    {
        using var warmupClient = Factory.CreateClient();
        using var warmupResponse = await warmupClient.GetAsync("/oauth2/jwks");

        using var scope = Factory.Services.CreateScope();
        var storeManagerFactory = scope.ServiceProvider.GetRequiredService<IStoreManagerFactory>();
        await using var storeManager = await storeManagerFactory.CreateAsync(
            CancellationToken.None
        );
        var store = storeManager.GetStore<ITenantStore>();

        var rootTenant = await store.GetOrDefaultAsync(
            RootTenantOptions.DefaultTenantId,
            CancellationToken.None
        );

        Assert.Null(rootTenant);
    }
}
