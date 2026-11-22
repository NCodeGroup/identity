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
public sealed class MasterKeyStoreWrappedByKeyPageTests : IDisposable
{
    private static DateTimeOffset CreatedWhen { get; } = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider Provider { get; }

    public MasterKeyStoreWrappedByKeyPageTests()
    {
        var serviceCollection = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(databaseName)
        );
        Provider = serviceCollection.BuildServiceProvider();
    }

    public void Dispose() => Provider.Dispose();

    private static PersistedMasterKey CreateMasterKey(
        string ownerId,
        string rootKeyProvider,
        string? rootKeyReference
    ) =>
        new()
        {
            KeyId = Guid.NewGuid(),
            OwnerType = SecretOwnerTypes.Tenant,
            OwnerId = ownerId,
            Version = 1,
            CreatedWhen = CreatedWhen,
            RetiredWhen = null,
            RootKeyProvider = rootKeyProvider,
            RootKeyReference = rootKeyReference,
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

    #region GetWrappedByKeyPageAsync Tests

    [Fact]
    public async Task GetWrappedByKeyPage_ReturnsOnlyRowsUnderTheProviderAndReference()
    {
        var underTheKey = CreateMasterKey("tenant-one", "fake-kms", "ref-a");
        var otherReference = CreateMasterKey("tenant-two", "fake-kms", "ref-b");
        var otherProvider = CreateMasterKey("tenant-three", "other-kms", "ref-a");
        var deploymentRooted = CreateMasterKey("tenant-four", "fake-kms", null);

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var store = CreateStore(context);
        foreach (var key in new[] { underTheKey, otherReference, otherProvider, deploymentRooted })
            await store.AddAsync(key, CancellationToken.None);
        await context.SaveChangesAsync();

        var result = await store.GetWrappedByKeyPageAsync(
            "fake-kms",
            "ref-a",
            new PageRequest { Limit = 10 },
            CancellationToken.None
        );

        Assert.Equal(underTheKey.KeyId, Assert.Single(result.Items).KeyId);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetWrappedByKeyPage_WithMoreRowsThanTheLimit_PagesThemWithACursor()
    {
        var first = CreateMasterKey("tenant-one", "fake-kms", "ref-a");
        var second = CreateMasterKey("tenant-two", "fake-kms", "ref-a");
        var third = CreateMasterKey("tenant-three", "fake-kms", "ref-a");

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var store = CreateStore(context);
        foreach (var key in new[] { first, second, third })
            await store.AddAsync(key, CancellationToken.None);
        await context.SaveChangesAsync();

        var firstPage = await store.GetWrappedByKeyPageAsync(
            "fake-kms",
            "ref-a",
            new PageRequest { Limit = 2 },
            CancellationToken.None
        );

        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await store.GetWrappedByKeyPageAsync(
            "fake-kms",
            "ref-a",
            new PageRequest { Limit = 2, Cursor = firstPage.NextCursor },
            CancellationToken.None
        );

        Assert.Single(secondPage.Items);
        Assert.Null(secondPage.NextCursor);

        var seen = firstPage.Items.Concat(secondPage.Items).Select(item => item.KeyId).ToHashSet();
        Assert.Equal(new[] { first.KeyId, second.KeyId, third.KeyId }.ToHashSet(), seen);
    }

    #endregion
}
