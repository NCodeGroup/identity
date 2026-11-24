// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Accounts.Credentials;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Accounts.Stores;

// A local account belongs to one connection, whose user names and email addresses are unique within it. SQLite
// enforces the unique indexes, which the in-memory provider does not.
public sealed class LocalAccountStoreConnectionTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string ConnectionOne = "tenant-1-local";
    private const string ConnectionTwo = "tenant-1-second";

    private static byte[] Password { get; } = "correct horse battery staple"u8.ToArray();

    private static Func<string, CancellationToken, ValueTask<bool>> NotRefused { get; } =
        static (_, _) => ValueTask.FromResult(false);

    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private IDisposable TenantScope { get; }
    private LocalAccountStore Store { get; }

    public LocalAccountStoreConnectionTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options => options.UseSqlite(Connection));

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        DbContext.Database.EnsureCreated();
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        var tenant = DbContext.SeedTenant(keyGenerator, TenantId);
        DbContext.SeedConnection(keyGenerator, tenant, ConnectionTwo, "second");
        TenantScope = Provider.GetRequiredService<TestAmbientTenantAccessor>().BeginScope(TenantId);
        Store = new LocalAccountStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            new PlainPasswordHasher(),
            DbContext
        );
    }

    public void Dispose()
    {
        TenantScope.Dispose();
        DbContext.Dispose();
        Provider.Dispose();
        Connection.Dispose();
    }

    #region Helpers

    private static PersistedLocalAccount CreateAccount(
        string connectionId,
        string localAccountId,
        string? userName,
        string? email = null
    ) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = connectionId,
            LocalAccountId = localAccountId,
            UserName = userName,
            DisplayName = "Alice Example",
            Email = email,
            EmailVerified = false,
            SecurityStamp = "stamp",
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    private async ValueTask AddAndSaveAsync(
        PersistedLocalAccount account,
        CancellationToken cancellationToken
    )
    {
        await Store.AddAsync(account, Password, cancellationToken);
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

    #region Uniqueness Tests

    [Fact]
    public async Task AddAsync_WhenTwoConnectionsHoldTheSameUserNameAndEmail_PersistsBoth()
    {
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-1", "alice", "alice@example.com"),
            CancellationToken.None
        );
        await AddAndSaveAsync(
            CreateAccount(ConnectionTwo, "account-2", "alice", "alice@example.com"),
            CancellationToken.None
        );

        var first = await Store.GetByUserNameOrDefaultAsync(
            ConnectionOne,
            "alice",
            CancellationToken.None
        );
        var second = await Store.GetByUserNameOrDefaultAsync(
            ConnectionTwo,
            "alice",
            CancellationToken.None
        );

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("account-1", first.LocalAccountId);
        Assert.Equal(ConnectionOne, first.ConnectionId);
        Assert.Equal("account-2", second.LocalAccountId);
        Assert.Equal(ConnectionTwo, second.ConnectionId);
    }

    [Fact]
    public async Task AddAsync_WhenAConnectionHoldsTheUserNameTwice_FailsOnSave()
    {
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-1", "alice"),
            CancellationToken.None
        );

        await Store.AddAsync(
            CreateAccount(ConnectionOne, "account-2", "Alice"),
            password: null,
            CancellationToken.None
        );

        await Assert.ThrowsAsync<DbUpdateException>(async () => await DbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task AddAsync_WhenAConnectionHoldsTheEmailTwice_FailsOnSave()
    {
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-1", "alice", "shared@example.com"),
            CancellationToken.None
        );

        await Store.AddAsync(
            CreateAccount(ConnectionOne, "account-2", "bob", "Shared@Example.com"),
            password: null,
            CancellationToken.None
        );

        await Assert.ThrowsAsync<DbUpdateException>(async () => await DbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task AddAsync_WhenAccountsHaveNoUserNameOrEmail_PersistsThemAll()
    {
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-1", userName: null, email: "a@example.com"),
            CancellationToken.None
        );
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-2", userName: null, email: "b@example.com"),
            CancellationToken.None
        );
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-3", userName: "carol"),
            CancellationToken.None
        );
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-4", userName: "dave"),
            CancellationToken.None
        );

        var page = await Store.GetPageAsync(
            ConnectionOne,
            new PageRequest { Limit = 10 },
            CancellationToken.None
        );

        Assert.Equal(4, page.Items.Count);
    }

    #endregion

    #region Lookup Tests

    [Fact]
    public async Task GetByEmailOrDefaultAsync_WhenTheConnectionHoldsTheEmail_ReturnsOnlyThatConnectionsAccount()
    {
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-1", "alice", "Alice@Example.com"),
            CancellationToken.None
        );
        await AddAndSaveAsync(
            CreateAccount(ConnectionTwo, "account-2", "alice2", "alice@example.com"),
            CancellationToken.None
        );

        var loaded = await Store.GetByEmailOrDefaultAsync(
            ConnectionTwo,
            "ALICE@example.com",
            CancellationToken.None
        );
        var missing = await Store.GetByEmailOrDefaultAsync(
            ConnectionOne,
            "other@example.com",
            CancellationToken.None
        );

        Assert.NotNull(loaded);
        Assert.Equal("account-2", loaded.LocalAccountId);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetByUserNameOrDefaultAsync_WhenTheConnectionIsUnknown_ReturnsNull()
    {
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-1", "alice"),
            CancellationToken.None
        );

        var loaded = await Store.GetByUserNameOrDefaultAsync(
            "missing",
            "alice",
            CancellationToken.None
        );

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetPageAsync_WhenConnectionsHoldAccounts_PagesEachConnectionAlone()
    {
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-1", "alice"),
            CancellationToken.None
        );
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-2", "bob"),
            CancellationToken.None
        );
        await AddAndSaveAsync(
            CreateAccount(ConnectionTwo, "account-3", "carol"),
            CancellationToken.None
        );

        var firstPage = await Store.GetPageAsync(
            ConnectionOne,
            new PageRequest { Limit = 1 },
            CancellationToken.None
        );
        Assert.NotNull(firstPage.NextCursor);
        var secondPage = await Store.GetPageAsync(
            ConnectionOne,
            new PageRequest { Limit = 1, Cursor = firstPage.NextCursor },
            CancellationToken.None
        );
        var otherPage = await Store.GetPageAsync(
            ConnectionTwo,
            new PageRequest { Limit = 10 },
            CancellationToken.None
        );

        Assert.Equal("account-1", Assert.Single(firstPage.Items).LocalAccountId);
        Assert.Equal("account-2", Assert.Single(secondPage.Items).LocalAccountId);
        Assert.Null(secondPage.NextCursor);
        Assert.Equal("account-3", Assert.Single(otherPage.Items).LocalAccountId);
    }

    #endregion

    #region VerifyCredentialAsync Tests

    [Fact]
    public async Task VerifyCredentialAsync_WhenTheIdentifierIsAnEmail_VerifiesTheAccountOfTheConnection()
    {
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-1", userName: null, email: "alice@example.com"),
            CancellationToken.None
        );

        var result = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier
            {
                ConnectionId = ConnectionOne,
                Email = "Alice@example.com",
            },
            Password,
            NotRefused,
            CancellationToken.None
        );
        var otherConnection = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier
            {
                ConnectionId = ConnectionTwo,
                Email = "alice@example.com",
            },
            Password,
            NotRefused,
            CancellationToken.None
        );

        Assert.True(result.IsVerified);
        Assert.Equal("account-1", result.Account.LocalAccountId);
        Assert.False(otherConnection.IsVerified);
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenAnAccountMatches_PassesItsKeyToTheGate()
    {
        await AddAndSaveAsync(
            CreateAccount(ConnectionOne, "account-1", "alice", "alice@example.com"),
            CancellationToken.None
        );
        var askedKeys = new List<string>();

        var result = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionOne, UserName = "ALICE" },
            Password,
            (accountKey, _) =>
            {
                askedKeys.Add(accountKey);
                return ValueTask.FromResult(false);
            },
            CancellationToken.None
        );

        Assert.Equal(LocalAccountAttemptKeys.ForAccount("account-1"), Assert.Single(askedKeys));
        Assert.Equal(LocalAccountAttemptKeys.ForAccount("account-1"), result.AccountKey);
        Assert.True(result.IsVerified);
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenNoAccountMatches_PassesTheSubmittedIdentifierToTheGate()
    {
        var askedKeys = new List<string>();

        var result = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionOne, UserName = "nobody" },
            Password,
            (accountKey, _) =>
            {
                askedKeys.Add(accountKey);
                return ValueTask.FromResult(false);
            },
            CancellationToken.None
        );

        Assert.Equal(LocalAccountAttemptKeys.ForIdentifier("nobody"), Assert.Single(askedKeys));
        Assert.Equal(LocalAccountAttemptKeys.ForIdentifier("nobody"), result.AccountKey);
        Assert.False(result.IsVerified);
    }

    #endregion
}
