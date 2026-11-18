// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

/// <summary>
/// Tests how the adapter reads and writes a tenant's local accounts as a whole, against a real
/// <see cref="UserManager{TUser}"/> over ASP.NET Core Identity's Entity Framework store.
/// </summary>
public sealed class AspNetIdentityLocalAccountStoreTransferTests : IAsyncLifetime
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string AccountA = "account-a";
    private const string AccountB = "account-b";
    private const string UserA = "alice";
    private const string UserB = "bob";
    private const string AccountC = "account-c";
    private const string UserC = "carol";
    private const string NormalizedUserC = "tenant-a|CAROL";
    private const string StoredSecurityStamp = "stored-stamp";

    private static byte[] Password { get; } = "correct horse battery staple"u8.ToArray();

    private static Func<string, CancellationToken, ValueTask<bool>> NotRefused { get; } =
        static (_, _) => ValueTask.FromResult(false);

    private ServiceProvider Provider { get; }
    private SaveChangesCounter SaveCounter { get; } = new();
    private TestAmbientTenantAccessor Accessor { get; } = new();
    private IDisposable TenantScope { get; }
    private ILocalAccountStore Store { get; }
    private ILocalAccountTransferStore TransferStore { get; }

    public AspNetIdentityLocalAccountStoreTransferTests()
    {
        TenantScope = Accessor.BeginScope(TenantA);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IAmbientTenantAccessor>(Accessor);
        var databaseName = Guid.NewGuid().ToString("N");
        serviceCollection.AddDbContext<IdentityDbContext<TestTenantUser>>(options =>
            options.UseInMemoryDatabase(databaseName).AddInterceptors(SaveCounter)
        );
        serviceCollection
            .AddIdentityCore<TestTenantUser>()
            .AddEntityFrameworkStores<IdentityDbContext<TestTenantUser>>();
        serviceCollection
            .NewBuilder<StoreManagerLibrary>()
            .AddAspNetIdentityLocalAccountStore<TestTenantUser>();

        Provider = serviceCollection.BuildServiceProvider();

        var storeProvider = new Mock<IStoreProvider>(MockBehavior.Strict).Object;
        Store = Provider
            .GetRequiredService<Func<IStoreProvider, ILocalAccountStore>>()
            .Invoke(storeProvider);
        TransferStore = Provider
            .GetRequiredService<Func<IStoreProvider, ILocalAccountTransferStore>>()
            .Invoke(storeProvider);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        TenantScope.Dispose();
        await Provider.DisposeAsync();
    }

    #region Helpers

    private static PersistedLocalAccount CreateAccount(
        string localAccountId,
        string userName,
        bool isEnabled = true,
        params PersistedLocalAccountClaim[] claims
    ) =>
        new()
        {
            TenantId = string.Empty,
            LocalAccountId = localAccountId,
            UserName = userName,
            Email = $"{userName}@example.com",
            EmailVerified = true,
            IsEnabled = isEnabled,
            Claims = claims,
            ConcurrencyToken = string.Empty,
        };

    private static LocalAccountTransferRecord CreateRecord(
        string localAccountId,
        string userName,
        params PersistedLocalAccountClaim[] claims
    ) =>
        new()
        {
            LocalAccountId = localAccountId,
            UserName = userName,
            NormalizedUserName = userName,
            Email = null,
            NormalizedEmail = null,
            EmailVerified = false,
            PasswordHash = null,
            SecurityStamp = "imported-stamp",
            IsEnabled = true,
            LockoutEnabled = false,
            LockoutEnd = null,
            AccessFailedCount = 0,
            Claims = claims,
        };

    #endregion

    #region ExportAsync Tests

    [Fact]
    public async Task ExportAsync_WhenTheTenantHasAccounts_ReturnsOnlyThatTenantsAccountsWithTheirCredentials()
    {
        var claim = new PersistedLocalAccountClaim { Type = "role", Value = "reader" };
        await Store.AddAsync(
            CreateAccount(AccountA, UserA, isEnabled: true, claim),
            Password,
            CancellationToken.None
        );
        using (Accessor.BeginScope(TenantB))
        {
            await Store.AddAsync(CreateAccount(AccountB, UserB), Password, CancellationToken.None);
        }

        var records = await TransferStore.ExportAsync(CancellationToken.None);

        var record = Assert.Single(records);
        Assert.Equal(AccountA, record.LocalAccountId);
        Assert.Equal(UserA, record.UserName);
        Assert.Equal("alice@example.com", record.Email);
        Assert.True(record.EmailVerified);
        Assert.False(string.IsNullOrEmpty(record.PasswordHash));
        Assert.False(string.IsNullOrEmpty(record.SecurityStamp));
        Assert.True(record.IsEnabled);
        var exportedClaim = Assert.Single(record.Claims);
        Assert.Equal("role", exportedClaim.Type);
        Assert.Equal("reader", exportedClaim.Value);
    }

    [Theory]
    [InlineData(null, NormalizedUserC, StoredSecurityStamp, nameof(IdentityUser.UserName))]
    [InlineData(UserC, null, StoredSecurityStamp, nameof(IdentityUser.NormalizedUserName))]
    [InlineData(UserC, NormalizedUserC, null, nameof(IdentityUser.SecurityStamp))]
    [InlineData(UserC, NormalizedUserC, "", nameof(IdentityUser.SecurityStamp))]
    public async Task ExportAsync_WhenAStoredUserLacksAMemberTheUserManagerStores_Throws(
        string? userName,
        string? normalizedUserName,
        string? securityStamp,
        string memberName
    )
    {
        await StoredTestTenantUsers.AddAsync(
            Provider,
            new TestTenantUser
            {
                Id = AccountC,
                TenantId = TenantA,
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                SecurityStamp = securityStamp,
            },
            CancellationToken.None
        );
        var stored = await StoredTestTenantUsers.GetAsync(
            Provider,
            AccountC,
            CancellationToken.None
        );
        Assert.Equal(userName, stored.UserName);
        Assert.Equal(normalizedUserName, stored.NormalizedUserName);
        Assert.Equal(securityStamp, stored.SecurityStamp);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await TransferStore.ExportAsync(CancellationToken.None)
        );

        Assert.Equal(
            $"The stored local account '{AccountC}' has no {memberName}, which ASP.NET Core Identity's user manager stores with every user.",
            exception.Message
        );
    }

    [Fact]
    public async Task ExportAsync_WhenAStoredUserHasNoEmailOrPasswordHash_ExportsThemAsNull()
    {
        await StoredTestTenantUsers.AddAsync(
            Provider,
            new TestTenantUser
            {
                Id = AccountC,
                TenantId = TenantA,
                UserName = UserC,
                NormalizedUserName = NormalizedUserC,
                SecurityStamp = StoredSecurityStamp,
            },
            CancellationToken.None
        );

        var records = await TransferStore.ExportAsync(CancellationToken.None);

        var record = Assert.Single(records);
        Assert.Equal(AccountC, record.LocalAccountId);
        Assert.Equal(UserC, record.UserName);
        Assert.Equal(NormalizedUserC, record.NormalizedUserName);
        Assert.Equal(StoredSecurityStamp, record.SecurityStamp);
        Assert.Null(record.Email);
        Assert.Null(record.NormalizedEmail);
        Assert.Null(record.PasswordHash);
    }

    #endregion

    #region ImportAsync Tests

    [Fact]
    public async Task ImportAsync_WhenExportedFromAnotherTenant_KeepsTheIdPasswordAndSecurityStamp()
    {
        await Store.AddAsync(CreateAccount(AccountA, UserA), Password, CancellationToken.None);
        var before = await Store.GetByIdOrDefaultAsync(AccountA, CancellationToken.None);
        Assert.NotNull(before);
        var records = await TransferStore.ExportAsync(CancellationToken.None);
        await TransferStore.RemoveAllAsync(CancellationToken.None);

        using (Accessor.BeginScope(TenantB))
        {
            await TransferStore.ImportAsync(records, CancellationToken.None);

            var result = await Store.VerifyCredentialAsync(
                UserA,
                Password,
                NotRefused,
                CancellationToken.None
            );

            Assert.True(result.IsVerified);
            Assert.Equal(AccountA, result.Account.LocalAccountId);
            Assert.Equal(before.SecurityStamp, result.Account.SecurityStamp);
            Assert.True(result.Account.IsEnabled);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenTheRecordIsDisabled_KeepsTheAccountDisabled()
    {
        await Store.AddAsync(
            CreateAccount(AccountA, UserA, isEnabled: false),
            Password,
            CancellationToken.None
        );
        var records = await TransferStore.ExportAsync(CancellationToken.None);
        await TransferStore.RemoveAllAsync(CancellationToken.None);

        using (Accessor.BeginScope(TenantB))
        {
            await TransferStore.ImportAsync(records, CancellationToken.None);

            var account = await Store.GetByIdOrDefaultAsync(AccountA, CancellationToken.None);

            Assert.NotNull(account);
            Assert.False(account.IsEnabled);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenTheRecordHasClaims_AddsThem()
    {
        var claim = new PersistedLocalAccountClaim { Type = "role", Value = "reader" };

        await TransferStore.ImportAsync(
            [CreateRecord(AccountA, UserA, claim)],
            CancellationToken.None
        );

        var account = await Store.GetByIdOrDefaultAsync(AccountA, CancellationToken.None);
        Assert.NotNull(account);
        Assert.Equal("imported-stamp", account.SecurityStamp);
        var imported = Assert.Single(account.Claims);
        Assert.Equal("role", imported.Type);
        Assert.Equal("reader", imported.Value);
    }

    [Fact]
    public async Task ImportAsync_WhenTheRecordsHaveClaims_WritesEachAccountTwice()
    {
        var claim = new PersistedLocalAccountClaim { Type = "role", Value = "reader" };

        await TransferStore.ImportAsync(
            [CreateRecord(AccountA, UserA, claim), CreateRecord(AccountB, UserB, claim)],
            CancellationToken.None
        );

        Assert.Equal(4, SaveCounter.Count);
        foreach (var localAccountId in new[] { AccountA, AccountB })
        {
            var account = await Store.GetByIdOrDefaultAsync(localAccountId, CancellationToken.None);
            Assert.NotNull(account);
            Assert.Equal("imported-stamp", account.SecurityStamp);
            var imported = Assert.Single(account.Claims);
            Assert.Equal("role", imported.Type);
            Assert.Equal("reader", imported.Value);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenNoTenantScopeIsSet_Throws()
    {
        TenantScope.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await TransferStore.ImportAsync([CreateRecord(AccountA, UserA)], CancellationToken.None)
        );
    }

    #endregion

    #region RemoveAllAsync Tests

    [Fact]
    public async Task RemoveAllAsync_WhenTheTenantHasAccounts_RemovesThemAndLeavesOtherTenantsUntouched()
    {
        await Store.AddAsync(CreateAccount(AccountA, UserA), Password, CancellationToken.None);
        using (Accessor.BeginScope(TenantB))
        {
            await Store.AddAsync(CreateAccount(AccountB, UserB), Password, CancellationToken.None);
        }

        await TransferStore.RemoveAllAsync(CancellationToken.None);

        Assert.Empty(await TransferStore.ExportAsync(CancellationToken.None));
        using (Accessor.BeginScope(TenantB))
        {
            var remaining = Assert.Single(await TransferStore.ExportAsync(CancellationToken.None));
            Assert.Equal(AccountB, remaining.LocalAccountId);
        }
    }

    #endregion
}
