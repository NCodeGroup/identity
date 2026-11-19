// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// Uses SQLite because the EF InMemory provider does not enforce unique indexes or concurrency tokens, which keep two
// replicas from storing two root keys for one owner (ADR-1414).
public sealed class OwnerRootKeyStoreTests : IDisposable
{
    private static DateTimeOffset Now { get; } = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }

    public OwnerRootKeyStoreTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options => options.UseSqlite(Connection));
        Provider = serviceCollection.BuildServiceProvider();

        using var scope = Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OpenIdDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        Provider.Dispose();
        Connection.Dispose();
    }

    #region Helpers

    private OwnerRootKeyStore CreateStore(OpenIdDbContext context) =>
        new(
            Mock.Of<IStoreProvider>(),
            Provider.GetRequiredService<ISurrogateKeyGenerator>(),
            TestPageCursorProtector.Instance,
            context
        );

    private static PersistedOwnerRootKey CreateRootKey(
        string ownerId,
        string state = OwnerRootKeyStates.Active,
        DateTimeOffset? validatedWhen = null,
        DateTimeOffset? failedWhen = null,
        string ownerType = SecretOwnerTypes.Tenant
    ) =>
        new()
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            KeyWrapperName = "aws-kms",
            KeyReference = $"arn:aws:kms:us-west-2:111122223333:key/{ownerId}",
            AccessIdentity = $"arn:aws:iam::111122223333:role/{ownerId}",
            State = state,
            CreatedWhen = Now.AddDays(-1),
            StateChangedWhen = Now.AddHours(-3),
            ValidatedWhen = validatedWhen,
            FailedWhen = failedWhen,
            Failure = failedWhen is null ? null : "AccessDenied: revoked",
        };

    private async ValueTask AddAsync(
        PersistedOwnerRootKey[] rootKeys,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var store = CreateStore(context);
        foreach (var rootKey in rootKeys)
            await store.AddAsync(rootKey, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    private async ValueTask<PersistedOwnerRootKey?> FindAsync(
        SecretOwner owner,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        return await CreateStore(context).FindAsync(owner, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<PersistedOwnerRootKey>> FindManyAsync(
        string ownerType,
        IReadOnlyList<string> ownerIds,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        return await CreateStore(context).FindManyAsync(ownerType, ownerIds, cancellationToken);
    }

    private async ValueTask<PagedResult<PersistedOwnerRootKey>> GetOwnerTypePageAsync(
        string ownerType,
        PageRequest page,
        CancellationToken cancellationToken
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        return await CreateStore(context).GetOwnerTypePageAsync(ownerType, page, cancellationToken);
    }

    #endregion

    #region AddAsync and FindAsync Tests

    [Fact]
    public async Task AddThenFind_RoundTripsEveryMember()
    {
        var expected = CreateRootKey("Tenant-One", validatedWhen: Now.AddMinutes(-5));
        expected.FailedWhen = Now.AddMinutes(-65);
        expected.Failure = "earlier failure";
        await AddAsync([expected], CancellationToken.None);

        var actual = await FindAsync(SecretOwner.ForTenant("Tenant-One"), CancellationToken.None);

        Assert.NotNull(actual);
        Assert.Equal(SecretOwnerTypes.Tenant, actual.OwnerType);
        Assert.Equal("Tenant-One", actual.OwnerId);
        Assert.Equal(expected.KeyWrapperName, actual.KeyWrapperName);
        Assert.Equal(expected.KeyReference, actual.KeyReference);
        Assert.Equal(expected.AccessIdentity, actual.AccessIdentity);
        Assert.NotNull(actual.AccessIdentity);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.CreatedWhen, actual.CreatedWhen);
        Assert.Equal(expected.StateChangedWhen, actual.StateChangedWhen);
        Assert.Equal(expected.ValidatedWhen, actual.ValidatedWhen);
        Assert.Equal(expected.FailedWhen, actual.FailedWhen);
        Assert.Equal("earlier failure", actual.Failure);
        Assert.Equal(expected.ConcurrencyToken, actual.ConcurrencyToken);
        Assert.False(string.IsNullOrEmpty(actual.ConcurrencyToken));
    }

    [Fact]
    public async Task Find_MatchesTheOwnerWithoutRegardToCase()
    {
        await AddAsync([CreateRootKey("Tenant-One")], CancellationToken.None);

        Assert.NotNull(
            await FindAsync(new SecretOwner("TENANT", "tenant-one"), CancellationToken.None)
        );
        Assert.Null(await FindAsync(SecretOwner.ForTenant("tenant-two"), CancellationToken.None));
        Assert.Null(
            await FindAsync(
                new SecretOwner(SecretOwnerTypes.Platform, "tenant-one"),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task Add_SecondRootKeyForTheSameOwner_ViolatesTheUniqueIndex()
    {
        await AddAsync([CreateRootKey("Tenant-One")], CancellationToken.None);

        await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
            await AddAsync([CreateRootKey("TENANT-ONE")], CancellationToken.None)
        );
    }

    #endregion

    #region FindManyAsync Tests

    [Fact]
    public async Task FindManyAsync_WhenSomeOwnersHaveKeys_ReturnsThoseKeys()
    {
        await AddAsync(
            [
                CreateRootKey("Team-One", ownerType: SecretOwnerTypes.Team),
                CreateRootKey("team-two", ownerType: SecretOwnerTypes.Team),
                CreateRootKey("team-three", ownerType: SecretOwnerTypes.Team),
                CreateRootKey("team-two"),
            ],
            CancellationToken.None
        );

        var actual = await FindManyAsync(
            SecretOwnerTypes.Team,
            ["TEAM-ONE", "team-two", "team-four"],
            CancellationToken.None
        );

        Assert.Equal(["Team-One", "team-two"], actual.Select(key => key.OwnerId));
        Assert.All(actual, key => Assert.Equal(SecretOwnerTypes.Team, key.OwnerType));
    }

    #endregion

    #region GetOwnerTypePageAsync Tests

    [Fact]
    public async Task GetOwnerTypePageAsync_WhenKeysSpanTwoPages_ReturnsEachOnce()
    {
        await AddAsync(
            [
                CreateRootKey("team-a", ownerType: SecretOwnerTypes.Team),
                CreateRootKey("tenant-a"),
                CreateRootKey("team-b", ownerType: SecretOwnerTypes.Team),
                CreateRootKey("tenant-b"),
                CreateRootKey("team-c", ownerType: SecretOwnerTypes.Team),
            ],
            CancellationToken.None
        );

        var first = await GetOwnerTypePageAsync(
            SecretOwnerTypes.Team,
            new PageRequest { Limit = 2 },
            CancellationToken.None
        );
        var second = await GetOwnerTypePageAsync(
            SecretOwnerTypes.Team,
            new PageRequest { Limit = 2, Cursor = first.NextCursor },
            CancellationToken.None
        );

        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
        Assert.Equal(
            ["team-a", "team-b", "team-c"],
            first.Items.Concat(second.Items).Select(key => key.OwnerId).Order()
        );
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task Update_WithTheCurrentToken_ChangesTheRowAndRenewsTheToken()
    {
        await AddAsync([CreateRootKey("Tenant-One")], CancellationToken.None);
        var current = await FindAsync(SecretOwner.ForTenant("tenant-one"), CancellationToken.None);
        Assert.NotNull(current);
        var originalToken = current.ConcurrencyToken;

        using (var scope = Provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
            current.State = OwnerRootKeyStates.Unavailable;
            current.FailedWhen = Now;
            current.Failure = "AccessDenied: revoked";
            current.AccessIdentity = null;
            await CreateStore(context).UpdateAsync(current, CancellationToken.None);
            await context.SaveChangesAsync();
        }

        var updated = await FindAsync(SecretOwner.ForTenant("tenant-one"), CancellationToken.None);
        Assert.Equal(OwnerRootKeyStates.Unavailable, updated?.State);
        Assert.Equal(Now, updated?.FailedWhen);
        Assert.Equal("AccessDenied: revoked", updated?.Failure);
        Assert.NotNull(updated);
        Assert.Null(updated.AccessIdentity);
        Assert.NotEqual(originalToken, updated?.ConcurrencyToken);
    }

    [Fact]
    public async Task Update_WithAStaleToken_ThrowsStoreConcurrencyException()
    {
        await AddAsync([CreateRootKey("Tenant-One")], CancellationToken.None);
        var current = await FindAsync(SecretOwner.ForTenant("tenant-one"), CancellationToken.None);
        Assert.NotNull(current);
        current.ConcurrencyToken = "stale";

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();

        await Assert.ThrowsAsync<StoreConcurrencyException>(async () =>
            await CreateStore(context).UpdateAsync(current, CancellationToken.None)
        );
    }

    [Fact]
    public async Task Update_OfARowThatIsGone_ThrowsInvalidOperationException()
    {
        var missing = CreateRootKey("Tenant-One");
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateStore(context).UpdateAsync(missing, CancellationToken.None)
        );
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task Remove_WithTheCurrentToken_RemovesTheRow()
    {
        await AddAsync(
            [CreateRootKey("Tenant-One"), CreateRootKey("tenant-two")],
            CancellationToken.None
        );
        var current = await FindAsync(SecretOwner.ForTenant("tenant-one"), CancellationToken.None);
        Assert.NotNull(current);

        using (var scope = Provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
            var removed = await CreateStore(context).RemoveAsync(current, CancellationToken.None);
            await context.SaveChangesAsync();
            Assert.True(removed);
        }

        Assert.Null(await FindAsync(SecretOwner.ForTenant("tenant-one"), CancellationToken.None));
        Assert.NotNull(
            await FindAsync(SecretOwner.ForTenant("tenant-two"), CancellationToken.None)
        );
    }

    [Fact]
    public async Task Remove_OfARowThatIsGone_ReturnsFalse()
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();

        var removed = await CreateStore(context)
            .RemoveAsync(CreateRootKey("Tenant-One"), CancellationToken.None);

        Assert.False(removed);
    }

    [Fact]
    public async Task Remove_WithAStaleToken_ThrowsStoreConcurrencyException()
    {
        await AddAsync([CreateRootKey("Tenant-One")], CancellationToken.None);
        var current = await FindAsync(SecretOwner.ForTenant("tenant-one"), CancellationToken.None);
        Assert.NotNull(current);
        current.ConcurrencyToken = "stale";
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();

        await Assert.ThrowsAsync<StoreConcurrencyException>(async () =>
            await CreateStore(context).RemoveAsync(current, CancellationToken.None)
        );
    }

    #endregion
}
