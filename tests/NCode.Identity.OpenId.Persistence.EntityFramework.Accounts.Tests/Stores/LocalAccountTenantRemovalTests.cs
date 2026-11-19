// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Accounts.Credentials;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Accounts.Stores;

// A local account and its claims belong to their tenant through a foreign key, so the tenant store erases them with
// their tenant (ADR-1212). SQLite enforces the foreign key, which an account row left behind would violate.
public sealed class LocalAccountTenantRemovalTests : IDisposable
{
    private const string TenantOneId = "tenant-1";
    private const string TenantTwoId = "tenant-2";

    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private TestAmbientTenantAccessor Accessor { get; }
    private LocalAccountStore AccountStore { get; }
    private ITenantStore TenantStore { get; }

    public LocalAccountTenantRemovalTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options => options.UseSqlite(Connection));

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        DbContext.Database.EnsureCreated();
        Accessor = Provider.GetRequiredService<TestAmbientTenantAccessor>();

        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        DbContext.SeedTenant(keyGenerator, TenantOneId);
        DbContext.SeedTenant(keyGenerator, TenantTwoId);

        var storeProvider = Mock.Of<IStoreProvider>();
        AccountStore = new LocalAccountStore(
            storeProvider,
            keyGenerator,
            TestPageCursorProtector.Instance,
            Mock.Of<IPasswordHasher>(),
            DbContext
        );
        TenantStore = Provider
            .GetRequiredService<Func<IStoreProvider, OpenIdDbContext, ITenantStore>>()
            .Invoke(storeProvider, DbContext);
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
        Connection.Dispose();
    }

    #region Helpers

    private static PersistedLocalAccount CreateAccount(
        string tenantId,
        string localAccountId,
        string userName
    ) =>
        new()
        {
            TenantId = tenantId,
            LocalAccountId = localAccountId,
            UserName = userName,
            Email = null,
            EmailVerified = false,
            SecurityStamp = "stamp",
            IsEnabled = true,
            Claims = [new PersistedLocalAccountClaim { Type = "role", Value = "admin" }],
            ConcurrencyToken = string.Empty,
        };

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_TenantWithLocalAccounts_RemovesAccountsAndClaims()
    {
        await AccountStore.AddAsync(
            CreateAccount(TenantOneId, "account-1", "alice"),
            password: null,
            CancellationToken.None
        );
        await AccountStore.AddAsync(
            CreateAccount(TenantTwoId, "account-2", "bob"),
            password: null,
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using var scope = Accessor.BeginUnscoped("test");

        var removed = await TenantStore.RemoveAsync(TenantOneId, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        Assert.True(removed);

        var accounts = await DbContext.Set<LocalAccountEntity>().ToListAsync();
        var remainingAccount = Assert.Single(accounts);
        Assert.Equal(TenantTwoId, remainingAccount.NormalizedTenantId);

        var claims = await DbContext.Set<LocalAccountClaimEntity>().ToListAsync();
        var remainingClaim = Assert.Single(claims);
        Assert.Equal(TenantTwoId, remainingClaim.NormalizedTenantId);
    }

    #endregion
}
