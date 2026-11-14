// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using IdGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.GlobalDirectory;

public sealed class LocalPlatformDirectoryTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private IIdGenerator<long> IdGenerator { get; }
    private LocalPlatformDirectory Directory { get; }

    public LocalPlatformDirectoryTests()
    {
        MockAmbientTenantAccessor = Mocks.Create<IAmbientTenantAccessor>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );
        serviceCollection.AddDbContextFactory<OpenIdDbContext>();

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        IdGenerator = Provider.GetRequiredService<IIdGenerator<long>>();
        Directory = new LocalPlatformDirectory(
            Provider.GetRequiredService<IDbContextFactory<OpenIdDbContext>>(),
            MockAmbientTenantAccessor.Object
        );
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
        Mocks.VerifyAll();
    }

    private void SetupUnscoped()
    {
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped("directory"))
            .Returns(Mock.Of<IDisposable>())
            .Verifiable();
    }

    private void SeedPlatform(int maxTenants)
    {
        DbContext
            .Set<PlatformEntity>()
            .Add(
                new PlatformEntity
                {
                    Id = IdGenerator.CreateId(),
                    Key = OpenIdResourceIds.Platform,
                    ConcurrencyToken = string.Empty,
                    SettingsConcurrencyToken = string.Empty,
                    SettingsJson = JsonSerializer.SerializeToElement(
                        new Dictionary<string, object> { ["max_tenants"] = maxTenants }
                    ),
                }
            );
        DbContext.SaveChanges();
    }

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenCalled_WritesNothing()
    {
        await Directory.PublishAsync(
            new PlatformDocument { SettingsJson = """{"max_tenants":3}""" },
            CancellationToken.None
        );

        Assert.False(DbContext.ChangeTracker.HasChanges());
        Assert.Empty(await DbContext.Set<PlatformEntity>().ToListAsync());
    }

    #endregion

    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_WhenThePlatformRowExists_ReturnsItsSettingsAsTheDocument()
    {
        SetupUnscoped();
        SeedPlatform(3);

        var document = await Directory.GetOrDefaultAsync(CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal("""{"max_tenants":3}""", document.SettingsJson);
        Assert.Equal(0, document.Sequence);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenThereIsNoPlatformRow_ReturnsNull()
    {
        SetupUnscoped();

        var document = await Directory.GetOrDefaultAsync(CancellationToken.None);

        Assert.Null(document);
    }

    #endregion
}
