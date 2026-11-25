// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

public sealed class BaseStoreLocalLookupTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string ClientA = "client-a";

    private ServiceProvider Provider { get; }
    private TestAmbientTenantAccessor AmbientTenantAccessor { get; } = new();
    private OpenIdDbContext DbContext { get; }
    private ClientStore Store { get; }

    public BaseStoreLocalLookupTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<ISurrogateKeyGenerator>(
            new SequentialSurrogateKeyGenerator()
        );
        serviceCollection.AddSingleton<IPageCursorProtector>(TestPageCursorProtector.Instance);

        Provider = serviceCollection.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<OpenIdDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseApplicationServiceProvider(Provider)
            .Options;

        DbContext = new OpenIdDbContext(
            options,
            AmbientTenantAccessor,
            [new CoreModelContributor()],
            []
        );
        Store = new ClientStore(
            Mock.Of<IStoreProvider>(),
            Provider.GetRequiredService<ISurrogateKeyGenerator>(),
            TestPageCursorProtector.Instance,
            DbContext,
            TestCryptoServices.CreateResourceIdGenerator()
        );
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
    }

    #region Helpers

    // Tracks a client of tenant-a as an added entity that is not saved, so a lookup can find it only through the
    // tracked (local) entities and never through the database.
    private void TrackClientOfTenantA()
    {
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        using (AmbientTenantAccessor.BeginUnscoped("test"))
        {
            var tenant = DbContext.SeedTenant(keyGenerator, TenantA);

            DbContext.Clients.Add(
                new ClientEntity
                {
                    Id = keyGenerator.CreateKey(),
                    TenantId = tenant.Id,
                    NormalizedTenantId = tenant.NormalizedTenantId,
                    Tenant = tenant,
                    ClientId = ClientA,
                    NormalizedClientId = ClientA,
                    PrincipalId = "principal-a",
                    NormalizedPrincipalId = "principal-a",
                    OwningNodeType = null,
                    OwningNodeId = null,
                    NormalizedOwningNodeId = null,
                    ResourceServerId = null,
                    ResourceServer = null,
                    ConcurrencyToken = string.Empty,
                    SettingsConcurrencyToken = string.Empty,
                    SecretsConcurrencyToken = string.Empty,
                    IsDisabled = false,
                    SettingsJson = System.Text.Json.JsonSerializer.SerializeToElement(
                        new Dictionary<string, object>()
                    ),
                    Secrets = [],
                }
            );
        }
    }

    #endregion

    [Fact]
    public async Task GetLocalOrDefault_UnsetScope_ReturnsNoTenantRow()
    {
        TrackClientOfTenantA();

        var client = await Store.GetOrDefaultAsync(ClientA, CancellationToken.None);

        Assert.Null(client);
    }

    [Fact]
    public async Task GetLocalOrDefault_OtherTenantScope_ReturnsNoRow()
    {
        TrackClientOfTenantA();

        using var scope = AmbientTenantAccessor.BeginScope(TenantB);
        var client = await Store.GetOrDefaultAsync(ClientA, CancellationToken.None);

        Assert.Null(client);
    }

    [Fact]
    public async Task GetLocalOrDefault_TenantScope_ReturnsTrackedRow()
    {
        TrackClientOfTenantA();

        using var scope = AmbientTenantAccessor.BeginScope(TenantA);
        var client = await Store.GetOrDefaultAsync(ClientA, CancellationToken.None);

        Assert.NotNull(client);
    }

    [Fact]
    public async Task GetLocalOrDefault_Unscoped_ReturnsTrackedRow()
    {
        TrackClientOfTenantA();

        using var scope = AmbientTenantAccessor.BeginUnscoped("test");
        var client = await Store.GetOrDefaultAsync(ClientA, CancellationToken.None);

        Assert.NotNull(client);
        Assert.Equal(TenantA, client.TenantId);
    }

    private sealed class TestAmbientTenantAccessor : IAmbientTenantAccessor
    {
        public string? TenantId { get; private set; }

        public bool IsScoped => TenantId is not null;

        public bool IsUnscoped { get; private set; }

        public string? UnscopedReason { get; private set; }

        public IDisposable BeginScope(string tenantId)
        {
            var previous = TenantId;
            TenantId = tenantId;
            return new Scope(() => TenantId = previous);
        }

        public IDisposable BeginUnscoped(string reason)
        {
            var previousUnscoped = IsUnscoped;
            var previousReason = UnscopedReason;
            IsUnscoped = true;
            UnscopedReason = reason;
            return new Scope(() =>
            {
                IsUnscoped = previousUnscoped;
                UnscopedReason = previousReason;
            });
        }

        private sealed class Scope(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}
