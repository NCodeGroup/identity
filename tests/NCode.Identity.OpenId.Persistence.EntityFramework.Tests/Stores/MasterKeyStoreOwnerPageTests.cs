// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// Uses the in-memory provider, as MasterKeyStoreActivePageTests does.
public sealed class MasterKeyStoreOwnerPageTests : IDisposable
{
    private static DateTimeOffset CreatedWhen { get; } = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider Provider { get; }

    public MasterKeyStoreOwnerPageTests()
    {
        var serviceCollection = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(databaseName)
        );
        Provider = serviceCollection.BuildServiceProvider();
    }

    public void Dispose() => Provider.Dispose();

    private static PersistedMasterKey CreateMasterKey(string ownerId, int version, bool retired) =>
        new()
        {
            KeyId = Guid.NewGuid(),
            OwnerType = SecretOwnerTypes.Tenant,
            OwnerId = ownerId,
            Version = version,
            CreatedWhen = CreatedWhen,
            RetiredWhen = retired ? CreatedWhen.AddDays(1) : null,
            RootKeyProvider = "fake-kms",
            RootKeyReference = "ref-a",
            RootKeyId = "root-key",
            RootWrappedKey = "root-wrapped-key",
            RecoveryKeyId = null,
            RecoveryWrappedKey = null,
            KeyCheck = "key-check",
        };

    private MasterKeyStore CreateStore(OpenIdDbContext context)
    {
        var storeProvider = Mock.Of<IStoreProvider>();
        var surrogateKeyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        return new MasterKeyStore(
            storeProvider,
            surrogateKeyGenerator,
            TestPageCursorProtector.Instance,
            context
        );
    }

    #region GetOwnerPageAsync Tests

    [Fact]
    public async Task GetOwnerPage_ReturnsEachOwnerOnceAPage()
    {
        var active = CreateMasterKey("tenant-one", version: 2, retired: false);
        var retired = CreateMasterKey("tenant-one", version: 1, retired: true);
        var other = CreateMasterKey("tenant-two", version: 1, retired: false);

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var store = CreateStore(context);
        foreach (var key in new[] { active, retired, other })
            await store.AddAsync(key, CancellationToken.None);
        await context.SaveChangesAsync();

        var result = await store.GetOwnerPageAsync(
            new PageRequest { Limit = 10 },
            CancellationToken.None
        );

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(new SecretOwner(SecretOwnerTypes.Tenant, "tenant-one"), result.Items);
        Assert.Contains(new SecretOwner(SecretOwnerTypes.Tenant, "tenant-two"), result.Items);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetOwnerPage_WithMoreRowsThanTheLimit_PagesThemWithACursor()
    {
        var first = CreateMasterKey("tenant-one", version: 1, retired: false);
        var second = CreateMasterKey("tenant-two", version: 1, retired: true);
        var third = CreateMasterKey("tenant-three", version: 1, retired: false);

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var store = CreateStore(context);
        foreach (var key in new[] { first, second, third })
            await store.AddAsync(key, CancellationToken.None);
        await context.SaveChangesAsync();

        var firstPage = await store.GetOwnerPageAsync(
            new PageRequest { Limit = 2 },
            CancellationToken.None
        );

        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await store.GetOwnerPageAsync(
            new PageRequest { Limit = 2, Cursor = firstPage.NextCursor },
            CancellationToken.None
        );

        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);

        var seen = firstPage
            .Items.Concat(secondPage.Items)
            .Select(item => item.OwnerId)
            .ToHashSet();
        Assert.Equal(new[] { "tenant-one", "tenant-two", "tenant-three" }.ToHashSet(), seen);
    }

    #endregion
}
