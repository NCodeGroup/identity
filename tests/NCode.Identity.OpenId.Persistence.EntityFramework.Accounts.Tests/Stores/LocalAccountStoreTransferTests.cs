// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Accounts.Credentials;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Accounts.Stores;

public sealed class LocalAccountStoreTransferTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string AccountA = "account-a";
    private const string AccountB = "account-b";
    private const string UserA = "alice";
    private const string UserB = "bob";
    private const string ConnectionA = "tenant-a-local";
    private const string ConnectionB = "tenant-b-local";

    private static byte[] Password { get; } = "correct horse battery staple"u8.ToArray();

    private static Func<string, CancellationToken, ValueTask<bool>> NotRefused { get; } =
        static (_, _) => ValueTask.FromResult(false);

    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private TestAmbientTenantAccessor Accessor { get; }
    private LocalAccountStore Store { get; }

    public LocalAccountStoreTransferTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        Accessor = Provider.GetRequiredService<TestAmbientTenantAccessor>();
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        DbContext.SeedTenant(keyGenerator, TenantA);
        DbContext.SeedTenant(keyGenerator, TenantB);
        Store = new LocalAccountStore(
            new Mock<IStoreProvider>(MockBehavior.Strict).Object,
            keyGenerator,
            TestPageCursorProtector.Instance,
            new PlainPasswordHasher(),
            DbContext
        );
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
    }

    #region Helpers

    private static PersistedLocalAccount CreateAccount(
        string tenantId,
        string localAccountId,
        string userName,
        bool isEnabled = true,
        params PersistedLocalAccountClaim[] claims
    ) =>
        new()
        {
            TenantId = tenantId,
            ConnectionId = TestOpenIdDbContextSetup.LocalConnectionId(tenantId),
            LocalAccountId = localAccountId,
            UserName = userName,
            DisplayName = "Alice Example",
            Email = $"{userName}@example.com",
            EmailVerified = true,
            IsEnabled = isEnabled,
            Claims = claims,
            ConcurrencyToken = string.Empty,
        };

    // Adds the account to its tenant with a password, then detaches so later reads hit the database.
    private async ValueTask AddAsync(
        PersistedLocalAccount account,
        CancellationToken cancellationToken
    )
    {
        await Store.AddAsync(account, Password, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    private async ValueTask SaveAndDetachAsync(CancellationToken cancellationToken)
    {
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    private sealed class PlainPasswordHasher : IPasswordHasher
    {
        public string DummyPasswordHash => "plain:dummy";

        public string HashPassword(ReadOnlySpan<byte> password) =>
            "plain:" + Convert.ToBase64String(password);

        public PasswordVerificationResult VerifyHashedPassword(
            string hashedPassword,
            ReadOnlySpan<byte> password
        ) =>
            string.Equals(hashedPassword, HashPassword(password), StringComparison.Ordinal)
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.Failed;
    }

    #endregion

    #region ExportAsync Tests

    [Fact]
    public async Task ExportAsync_WhenTheTenantHasAccounts_ReturnsOnlyThatTenantsAccountsWithTheirCredentials()
    {
        var claim = new PersistedLocalAccountClaim { Type = "role", Value = "reader" };
        await AddAsync(
            CreateAccount(TenantA, AccountA, UserA, isEnabled: true, claim),
            CancellationToken.None
        );
        await AddAsync(CreateAccount(TenantB, AccountB, UserB), CancellationToken.None);

        Accessor.TenantId = TenantA;
        var records = await Store.ExportAsync(ConnectionA, CancellationToken.None);

        var record = Assert.Single(records);
        Assert.Equal(AccountA, record.LocalAccountId);
        Assert.Equal(UserA, record.UserName);
        Assert.Equal(UserA, record.NormalizedUserName);
        Assert.Equal("Alice Example", record.DisplayName);
        Assert.Equal("alice@example.com", record.Email);
        Assert.Equal("alice@example.com", record.NormalizedEmail);
        Assert.True(record.EmailVerified);
        Assert.Equal("plain:" + Convert.ToBase64String(Password), record.PasswordHash);
        Assert.False(string.IsNullOrEmpty(record.SecurityStamp));
        Assert.True(record.IsEnabled);
        var exportedClaim = Assert.Single(record.Claims);
        Assert.Equal("role", exportedClaim.Type);
        Assert.Equal("reader", exportedClaim.Value);
    }

    [Fact]
    public async Task ExportAsync_WhenNoTenantScopeIsSet_ReturnsNothing()
    {
        await AddAsync(CreateAccount(TenantA, AccountA, UserA), CancellationToken.None);

        var records = await Store.ExportAsync(ConnectionA, CancellationToken.None);

        Assert.Empty(records);
    }

    #endregion

    #region ImportAsync Tests

    [Fact]
    public async Task ImportAsync_WhenExportedFromAnotherTenant_KeepsTheIdPasswordAndSecurityStamp()
    {
        await AddAsync(CreateAccount(TenantA, AccountA, UserA), CancellationToken.None);
        Accessor.TenantId = TenantA;
        var before = await Store.GetByIdOrDefaultAsync(AccountA, CancellationToken.None);
        Assert.NotNull(before);
        var records = await Store.ExportAsync(ConnectionA, CancellationToken.None);
        DbContext.ChangeTracker.Clear();

        Accessor.TenantId = TenantB;
        await Store.ImportAsync(ConnectionB, records, CancellationToken.None);
        await SaveAndDetachAsync(CancellationToken.None);

        var result = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionB, UserName = UserA },
            Password,
            NotRefused,
            CancellationToken.None
        );
        Assert.True(result.IsVerified);
        Assert.Equal(AccountA, result.Account.LocalAccountId);
        Assert.Equal(before.SecurityStamp, result.Account.SecurityStamp);
        Assert.Equal("tenant-b", result.Account.TenantId);
        Assert.Equal("Alice Example", result.Account.DisplayName);
        var wrong = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionB, UserName = UserA },
            "another long passphrase"u8.ToArray(),
            NotRefused,
            CancellationToken.None
        );
        Assert.False(wrong.IsVerified);
    }

    [Fact]
    public async Task ImportAsync_WhenTheRecordHasClaimsAndIsDisabled_KeepsThem()
    {
        var claim = new PersistedLocalAccountClaim { Type = "role", Value = "reader" };
        await AddAsync(
            CreateAccount(TenantA, AccountA, UserA, isEnabled: false, claim),
            CancellationToken.None
        );
        Accessor.TenantId = TenantA;
        var records = await Store.ExportAsync(ConnectionA, CancellationToken.None);
        DbContext.ChangeTracker.Clear();

        Accessor.TenantId = TenantB;
        await Store.ImportAsync(ConnectionB, records, CancellationToken.None);
        await SaveAndDetachAsync(CancellationToken.None);

        var account = await Store.GetByIdOrDefaultAsync(AccountA, CancellationToken.None);
        Assert.NotNull(account);
        Assert.False(account.IsEnabled);
        var imported = Assert.Single(account.Claims);
        Assert.Equal("role", imported.Type);
        Assert.Equal("reader", imported.Value);
    }

    [Fact]
    public async Task ImportAsync_WhenTheRecordCarriesAnotherStoresNormalizedNames_StoresTheNamesThisStoreNormalizes()
    {
        var record = new LocalAccountTransferRecord
        {
            LocalAccountId = AccountA,
            UserName = "Alice",
            DisplayName = "Alice Example",
            NormalizedUserName = "TENANT-A|ALICE",
            Email = "Alice@Example.com",
            NormalizedEmail = "TENANT-A|ALICE@EXAMPLE.COM",
            EmailVerified = true,
            PasswordHash = "plain:" + Convert.ToBase64String(Password),
            SecurityStamp = "stamp",
            IsEnabled = true,
            LockoutEnabled = false,
            LockoutEnd = null,
            AccessFailedCount = 0,
            Claims = [],
        };

        Accessor.TenantId = TenantB;
        await Store.ImportAsync(ConnectionB, [record], CancellationToken.None);
        await SaveAndDetachAsync(CancellationToken.None);

        var entity = await DbContext
            .Set<LocalAccountEntity>()
            .SingleAsync(x => x.LocalAccountId == AccountA, CancellationToken.None);
        Assert.Equal("alice", entity.NormalizedUserName);
        Assert.Equal("alice@example.com", entity.NormalizedEmail);
        Assert.NotNull(
            await Store.GetByUserNameOrDefaultAsync(ConnectionB, "ALICE", CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportAsync_WhenNoTenantScopeIsSet_Throws()
    {
        var record = new LocalAccountTransferRecord
        {
            LocalAccountId = AccountA,
            UserName = UserA,
            DisplayName = "Alice Example",
            NormalizedUserName = UserA,
            Email = null,
            NormalizedEmail = null,
            EmailVerified = false,
            PasswordHash = null,
            SecurityStamp = "stamp",
            IsEnabled = true,
            LockoutEnabled = false,
            LockoutEnd = null,
            AccessFailedCount = 0,
            Claims = [],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.ImportAsync(ConnectionB, [record], CancellationToken.None)
        );
    }

    #endregion

    #region RemoveAllAsync Tests

    [Fact]
    public async Task RemoveAllAsync_WhenTheTenantHasAccounts_RemovesThemAndLeavesOtherTenantsUntouched()
    {
        var claim = new PersistedLocalAccountClaim { Type = "role", Value = "reader" };
        await AddAsync(
            CreateAccount(TenantA, AccountA, UserA, isEnabled: true, claim),
            CancellationToken.None
        );
        await AddAsync(CreateAccount(TenantB, AccountB, UserB), CancellationToken.None);

        Accessor.TenantId = TenantA;
        await Store.RemoveAllAsync(ConnectionA, CancellationToken.None);
        await SaveAndDetachAsync(CancellationToken.None);

        Assert.Empty(await Store.ExportAsync(ConnectionA, CancellationToken.None));
        Accessor.TenantId = TenantB;
        var remaining = Assert.Single(await Store.ExportAsync(ConnectionB, CancellationToken.None));
        Assert.Equal(AccountB, remaining.LocalAccountId);
    }

    #endregion
}
