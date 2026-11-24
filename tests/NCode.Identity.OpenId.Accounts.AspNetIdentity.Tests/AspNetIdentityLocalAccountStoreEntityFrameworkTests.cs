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
/// Tests the adapter against a real <see cref="UserManager{TUser}"/> over ASP.NET Core Identity's Entity Framework
/// store, so the host's default password validators are in play.
/// </summary>
public sealed class AspNetIdentityLocalAccountStoreEntityFrameworkTests : IAsyncLifetime
{
    private const string UserName = "alice";
    private const string LocalAccountId = "user-1";
    private const string TenantId = "tenant-1";
    private const string ConnectionId = "connection-1";
    private const string StoredNormalizedUserName = "tenant-1|connection-1|ALICE";
    private const string StoredSecurityStamp = "stored-stamp";
    private const string ComposedPassword = "caf\u00E9 correct horse battery";
    private const string DecomposedPassword = "cafe\u0301 correct horse battery";

    // Passes Identity's default password validators (a digit, an upper-case letter, a lower-case letter and a symbol).
    private const string HostPassword = "Cafe\u0301-Correct-Horse-1";

    // Passwords that the password policy accepts but that Identity's default validators refuse (no digit and no
    // upper-case letter).
    private static byte[] FirstPassword { get; } = "correct horse battery staple"u8.ToArray();
    private static byte[] SecondPassword { get; } = "another long passphrase"u8.ToArray();
    private static byte[] ComposedPasswordBytes { get; } =
        "caf\u00E9 correct horse battery"u8.ToArray();
    private static byte[] DecomposedPasswordBytes { get; } =
        "cafe\u0301 correct horse battery"u8.ToArray();
    private static byte[] ComposedHostPasswordBytes { get; } =
        "Caf\u00E9-Correct-Horse-1"u8.ToArray();

    // A hasher that does not normalize, as the host's pages used before the adapter wrapped it; the user manager's
    // hasher normalizes.
    private static PasswordHasher<TestTenantUser> RawPasswordHasher { get; } = new();

    private static Func<string, CancellationToken, ValueTask<bool>> NotRefused { get; } =
        static (_, _) => ValueTask.FromResult(false);

    private ServiceProvider Provider { get; }
    private ILocalAccountStore Store { get; }
    private IDisposable TenantScope { get; }

    public AspNetIdentityLocalAccountStoreEntityFrameworkTests()
    {
        var accessor = new TestAmbientTenantAccessor();
        TenantScope = accessor.BeginScope(TenantId);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IAmbientTenantAccessor>(accessor);
        var databaseName = Guid.NewGuid().ToString("N");
        serviceCollection.AddDbContext<IdentityDbContext<TestTenantUser>>(options =>
            options.UseInMemoryDatabase(databaseName)
        );
        serviceCollection
            .AddIdentityCore<TestTenantUser>()
            .AddEntityFrameworkStores<IdentityDbContext<TestTenantUser>>();
        serviceCollection
            .NewBuilder<StoreManagerLibrary>()
            .AddAspNetIdentityLocalAccountStore<TestTenantUser>();

        Provider = serviceCollection.BuildServiceProvider();

        var storeManager = new Mock<IStoreManager>(MockBehavior.Strict).Object;
        var registration = Provider.GetRequiredService<LocalAccountStoreRegistration>();
        Store = registration.CreateStore(storeManager);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        TenantScope.Dispose();
        await Provider.DisposeAsync();
    }

    #region Helpers

    private static PersistedLocalAccount CreateAccount(
        bool isEnabled = true,
        params (string Type, string Value)[] claims
    ) =>
        new()
        {
            TenantId = string.Empty,
            ConnectionId = ConnectionId,
            LocalAccountId = LocalAccountId,
            UserName = UserName,
            DisplayName = "Alice Example",
            Email = "alice@example.com",
            EmailVerified = true,
            IsEnabled = isEnabled,
            Claims = claims
                .Select(claim => new PersistedLocalAccountClaim
                {
                    Type = claim.Type,
                    Value = claim.Value,
                })
                .ToList(),
            ConcurrencyToken = string.Empty,
        };

    private static PersistedLocalAccount CreateConnectionAccount(
        string connectionId,
        string localAccountId,
        string? userName,
        string? email
    ) =>
        new()
        {
            TenantId = string.Empty,
            ConnectionId = connectionId,
            LocalAccountId = localAccountId,
            UserName = userName,
            DisplayName = "Example",
            Email = email,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    // Stores the hash of the password exactly as given, without any normalization.
    private async ValueTask SetStoredPasswordHashAsync(
        string password,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        using var connectionScope = AmbientLookupConnection.BeginScope(ConnectionId);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TestTenantUser>>();
        var user = await userManager.FindByIdAsync(LocalAccountId);
        Assert.NotNull(user);
        user.PasswordHash = RawPasswordHasher.HashPassword(user, password);
        var updated = await userManager.UpdateAsync(user);
        Assert.True(updated.Succeeded);
    }

    private async ValueTask<PasswordVerificationResult> VerifyStoredPasswordHashAsync(
        string password,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TestTenantUser>>();
        var user = await userManager.FindByIdAsync(LocalAccountId);
        Assert.NotNull(user);
        Assert.NotNull(user.PasswordHash);
        return RawPasswordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
    }

    // Checks a password as the host's own ASP.NET Core Identity pages do, through the user manager.
    private async ValueTask<bool> CheckPasswordAsync(
        string password,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        using var connectionScope = AmbientLookupConnection.BeginScope(ConnectionId);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TestTenantUser>>();
        var user = await userManager.FindByIdAsync(LocalAccountId);
        Assert.NotNull(user);
        return await userManager.CheckPasswordAsync(user, password);
    }

    // Creates a user as the host's own ASP.NET Core Identity pages do, through the user manager.
    private async ValueTask CreateHostUserAsync(
        string password,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        using var connectionScope = AmbientLookupConnection.BeginScope(ConnectionId);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TestTenantUser>>();
        var user = new TestTenantUser(UserName)
        {
            Id = LocalAccountId,
            TenantId = TenantId,
            ConnectionId = ConnectionId,
        };
        var created = await userManager.CreateAsync(user, password);
        var errors = string.Join("; ", created.Errors.Select(error => error.Description));
        Assert.True(created.Succeeded, errors);
    }

    #endregion

    #region Connection Tests

    [Fact]
    public async Task AddAsync_WhenTwoConnectionsHoldTheSameUserName_KeepsEachAccountInItsConnection()
    {
        await Store.AddAsync(
            CreateConnectionAccount("connection-1", "user-1", UserName, "one@example.com"),
            FirstPassword,
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateConnectionAccount("connection-2", "user-2", UserName, "two@example.com"),
            SecondPassword,
            CancellationToken.None
        );

        var first = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = "connection-1", UserName = UserName },
            FirstPassword,
            NotRefused,
            CancellationToken.None
        );
        var second = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = "connection-2", UserName = UserName },
            SecondPassword,
            NotRefused,
            CancellationToken.None
        );
        var crossed = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = "connection-2", UserName = UserName },
            FirstPassword,
            NotRefused,
            CancellationToken.None
        );

        Assert.True(first.IsVerified);
        Assert.Equal("user-1", first.Account.LocalAccountId);
        Assert.Equal("connection-1", first.Account.ConnectionId);
        Assert.True(second.IsVerified);
        Assert.Equal("user-2", second.Account.LocalAccountId);
        Assert.Null(crossed.Account);
    }

    [Fact]
    public async Task GetByUserNameOrDefaultAsync_WhenAnotherConnectionHoldsTheUserName_ReturnsNull()
    {
        await Store.AddAsync(
            CreateConnectionAccount("connection-1", "user-1", UserName, null),
            password: null,
            CancellationToken.None
        );

        var found = await Store.GetByUserNameOrDefaultAsync(
            "connection-1",
            UserName,
            CancellationToken.None
        );
        var other = await Store.GetByUserNameOrDefaultAsync(
            "connection-2",
            UserName,
            CancellationToken.None
        );

        Assert.NotNull(found);
        Assert.Null(other);
    }

    [Fact]
    public async Task AddAsync_WhenTheAccountHasNoUserName_ReadsItBackWithoutOneAndFindsItByEmail()
    {
        await Store.AddAsync(
            CreateConnectionAccount("connection-1", "user-1", null, "carol@example.com"),
            FirstPassword,
            CancellationToken.None
        );

        var byId = await Store.GetByIdOrDefaultAsync("user-1", CancellationToken.None);
        var byEmail = await Store.GetByEmailOrDefaultAsync(
            "connection-1",
            "carol@example.com",
            CancellationToken.None
        );
        var byIdAsUserName = await Store.GetByUserNameOrDefaultAsync(
            "connection-1",
            "user-1",
            CancellationToken.None
        );
        var verified = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier
            {
                ConnectionId = "connection-1",
                Email = "carol@example.com",
            },
            FirstPassword,
            NotRefused,
            CancellationToken.None
        );

        Assert.NotNull(byId);
        Assert.Null(byId.UserName);
        Assert.NotNull(byEmail);
        Assert.Equal("user-1", byEmail.LocalAccountId);
        Assert.Null(byIdAsUserName);
        Assert.True(verified.IsVerified);
        Assert.Equal(LocalAccountAttemptKeys.ForAccount("user-1"), verified.AccountKey);
    }

    [Fact]
    public async Task GetByEmailOrDefaultAsync_WhenAnotherConnectionHoldsTheEmail_ReturnsNull()
    {
        await Store.AddAsync(
            CreateConnectionAccount("connection-1", "user-1", UserName, "alice@example.com"),
            password: null,
            CancellationToken.None
        );

        var other = await Store.GetByEmailOrDefaultAsync(
            "connection-2",
            "alice@example.com",
            CancellationToken.None
        );

        Assert.Null(other);
    }

    [Fact]
    public async Task GetPageAsync_WhenConnectionsHoldAccounts_ListsOnlyTheNamedConnection()
    {
        await Store.AddAsync(
            CreateConnectionAccount("connection-1", "user-1", "alice", null),
            password: null,
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateConnectionAccount("connection-2", "user-2", "alice", null),
            password: null,
            CancellationToken.None
        );

        var page = await Store.GetPageAsync(
            "connection-2",
            new PageRequest { Limit = 10 },
            CancellationToken.None
        );

        var item = Assert.Single(page.Items);
        Assert.Equal("user-2", item.LocalAccountId);
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_WhenIdentityPasswordValidatorsRefuseThePassword_StoresIt()
    {
        await Store.AddAsync(CreateAccount(), FirstPassword, CancellationToken.None);

        var result = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionId, UserName = UserName },
            FirstPassword,
            NotRefused,
            CancellationToken.None
        );

        Assert.True(result.IsVerified);
        Assert.Equal(LocalAccountId, result.Account.LocalAccountId);
    }

    #endregion

    #region GetByIdOrDefaultAsync Tests

    [Theory]
    [InlineData(null, StoredSecurityStamp, nameof(IdentityUser.UserName))]
    [InlineData(UserName, null, nameof(IdentityUser.SecurityStamp))]
    public async Task GetByIdOrDefaultAsync_WhenTheStoredUserLacksAMemberTheUserManagerStores_Throws(
        string? userName,
        string? securityStamp,
        string memberName
    )
    {
        await StoredTestTenantUsers.AddAsync(
            Provider,
            new TestTenantUser
            {
                Id = LocalAccountId,
                TenantId = TenantId,
                ConnectionId = ConnectionId,
                UserName = userName,
                NormalizedUserName = StoredNormalizedUserName,
                SecurityStamp = securityStamp,
            },
            CancellationToken.None
        );
        var stored = await StoredTestTenantUsers.GetAsync(
            Provider,
            LocalAccountId,
            CancellationToken.None
        );
        Assert.Equal(userName, stored.UserName);
        Assert.Equal(securityStamp, stored.SecurityStamp);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.GetByIdOrDefaultAsync(LocalAccountId, CancellationToken.None)
        );

        Assert.Equal(
            $"The stored local account '{LocalAccountId}' has no {memberName}, which ASP.NET Core Identity's user manager stores with every user.",
            exception.Message
        );
    }

    #endregion

    #region SetPasswordAsync Tests

    [Fact]
    public async Task SetPasswordAsync_WhenIdentityPasswordValidatorsRefuseThePassword_StoresItAndRotatesTheSecurityStamp()
    {
        await Store.AddAsync(CreateAccount(), FirstPassword, CancellationToken.None);
        var before = await Store.GetByIdOrDefaultAsync(LocalAccountId, CancellationToken.None);
        Assert.NotNull(before);

        await Store.SetPasswordAsync(LocalAccountId, SecondPassword, CancellationToken.None);

        var second = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionId, UserName = UserName },
            SecondPassword,
            NotRefused,
            CancellationToken.None
        );
        Assert.True(second.IsVerified);
        Assert.NotEqual(before.SecurityStamp, second.Account.SecurityStamp);
        var first = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionId, UserName = UserName },
            FirstPassword,
            NotRefused,
            CancellationToken.None
        );
        Assert.Null(first.Account);
    }

    #endregion

    #region VerifyCredentialAsync Tests

    [Fact]
    public async Task VerifyCredentialAsync_WhenTheComposedFormIsSubmitted_VerifiesAPasswordSetDecomposed()
    {
        await Store.AddAsync(CreateAccount(), DecomposedPasswordBytes, CancellationToken.None);

        var composed = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionId, UserName = UserName },
            ComposedPasswordBytes,
            NotRefused,
            CancellationToken.None
        );
        var decomposed = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionId, UserName = UserName },
            DecomposedPasswordBytes,
            NotRefused,
            CancellationToken.None
        );

        Assert.True(composed.IsVerified);
        Assert.Equal(LocalAccountId, composed.Account.LocalAccountId);
        Assert.True(decomposed.IsVerified);
        Assert.Equal(LocalAccountId, decomposed.Account.LocalAccountId);
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenTheStoredHashIsOfTheDecomposedForm_RehashesTheComposedForm()
    {
        await Store.AddAsync(CreateAccount(), password: null, CancellationToken.None);
        await SetStoredPasswordHashAsync(DecomposedPassword, CancellationToken.None);
        var before = await Store.GetByIdOrDefaultAsync(LocalAccountId, CancellationToken.None);
        Assert.NotNull(before);

        var result = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionId, UserName = UserName },
            DecomposedPasswordBytes,
            NotRefused,
            CancellationToken.None
        );

        Assert.True(result.IsVerified);
        Assert.Equal(before.SecurityStamp, result.Account.SecurityStamp);
        var verification = await VerifyStoredPasswordHashAsync(
            ComposedPassword,
            CancellationToken.None
        );
        Assert.Equal(PasswordVerificationResult.Success, verification);
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenNoUserHasTheUserName_ReturnsNull()
    {
        var result = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionId, UserName = "nobody" },
            FirstPassword,
            NotRefused,
            CancellationToken.None
        );

        Assert.Null(result.Account);
    }

    [Fact]
    public async Task VerifyCredentialAsync_WhenTheHostSetTheDecomposedForm_VerifiesTheComposedForm()
    {
        await CreateHostUserAsync(HostPassword, CancellationToken.None);

        var result = await Store.VerifyCredentialAsync(
            new LocalAccountSignInIdentifier { ConnectionId = ConnectionId, UserName = UserName },
            ComposedHostPasswordBytes,
            NotRefused,
            CancellationToken.None
        );

        Assert.True(result.IsVerified);
        Assert.Equal(LocalAccountId, result.Account.LocalAccountId);
    }

    #endregion

    #region CheckPasswordAsync Tests

    [Fact]
    public async Task CheckPasswordAsync_WhenTheAdapterStoredTheComposedForm_VerifiesTheDecomposedForm()
    {
        await Store.AddAsync(CreateAccount(), ComposedPasswordBytes, CancellationToken.None);

        var verified = await CheckPasswordAsync(DecomposedPassword, CancellationToken.None);

        Assert.True(verified);
    }

    [Fact]
    public async Task CheckPasswordAsync_WhenTheStoredHashIsOfTheDecomposedForm_ReplacesItWithTheComposedForm()
    {
        await Store.AddAsync(CreateAccount(), password: null, CancellationToken.None);
        await SetStoredPasswordHashAsync(DecomposedPassword, CancellationToken.None);

        var verified = await CheckPasswordAsync(DecomposedPassword, CancellationToken.None);

        Assert.True(verified);
        var verification = await VerifyStoredPasswordHashAsync(
            ComposedPassword,
            CancellationToken.None
        );
        Assert.Equal(PasswordVerificationResult.Success, verification);
    }

    #endregion
}
