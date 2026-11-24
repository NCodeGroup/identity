// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

public sealed class FederatedPrincipalStoreTests : IDisposable
{
    private const string TenantId = "tenant-1";

    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private FederatedPrincipalStore Store { get; }

    public FederatedPrincipalStoreTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        Store = new FederatedPrincipalStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
        DbContext.SeedTenant(keyGenerator, TenantId);
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
    }

    private static PersistedFederatedPrincipal CreatePrincipal(string principalId) =>
        new()
        {
            TenantId = TenantId,
            PrincipalId = principalId,
            ConcurrencyToken = string.Empty,
        };

    private static PersistedClient CreateClient(string clientId) =>
        new PersistedClientBuilder { TenantId = TenantId, ClientId = clientId }.Build();

    private static PersistedConsent CreateConsent(string clientId, string subjectId) =>
        new()
        {
            TenantId = TenantId,
            ClientId = clientId,
            SubjectId = subjectId,
            RequestedScopes = ["openid"],
            GrantedScopes = ["openid"],
            CreatedWhen = DateTimeOffset.UnixEpoch,
            UpdatedWhen = DateTimeOffset.UnixEpoch,
            ConcurrencyToken = string.Empty,
        };

    private static PersistedGrant CreateGrant(string clientId, string subjectId) =>
        new()
        {
            GrantType = "authorization_code",
            HashedKey = $"{subjectId}-hashed",
            GrantId = $"{subjectId}-grant",
            ConcurrencyToken = string.Empty,
            TenantId = TenantId,
            ClientId = clientId,
            SubjectId = subjectId,
            SessionId = null,
            RefreshTokenLineageId = null,
            CreatedWhen = DateTimeOffset.UnixEpoch,
            ExpiresWhen = DateTimeOffset.UnixEpoch.AddHours(1),
            RevokedWhen = null,
            ConsumedWhen = null,
            ProtectedPayload = "protected-payload",
        };

    [Fact]
    public async Task AddAsync_WhenPersisted_RoundTripsByPrincipalId()
    {
        await Store.AddAsync(CreatePrincipal("principal-1"), CancellationToken.None);
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetOrDefaultAsync("principal-1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("principal-1", loaded.PrincipalId);
        Assert.NotEmpty(loaded.ConcurrencyToken);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenCaseDiffers_IsCaseInsensitive()
    {
        await Store.AddAsync(CreatePrincipal("Principal-1"), CancellationToken.None);
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetOrDefaultAsync("principal-1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("Principal-1", loaded.PrincipalId);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenMissing_ReturnsNull()
    {
        var loaded = await Store.GetOrDefaultAsync("nope", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task AddAsync_WhenBlocked_RoundTripsTheBlockState()
    {
        var principal = CreatePrincipal("principal-1");
        principal.IsBlocked = true;
        await Store.AddAsync(principal, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var loaded = await Store.GetOrDefaultAsync("principal-1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.True(loaded.IsBlocked);
    }

    [Fact]
    public async Task UpdateAsync_WhenBlocked_PersistsTheBlockState()
    {
        await Store.AddAsync(CreatePrincipal("principal-1"), CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var loaded = await Store.GetOrDefaultAsync("principal-1", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.False(loaded.IsBlocked);
        loaded.IsBlocked = true;
        await Store.UpdateAsync(loaded, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var reloaded = await Store.GetOrDefaultAsync("principal-1", CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.True(reloaded.IsBlocked);
    }

    #region GetByIdsAsync Tests

    [Fact]
    public async Task GetByIdsAsync_WhenSomeIdentifiersMatch_ReturnsOnlyTheMatchingPrincipals()
    {
        var seeded1 = CreatePrincipal("principal-1");
        await Store.AddAsync(seeded1, CancellationToken.None);
        var seeded2 = CreatePrincipal("principal-2");
        await Store.AddAsync(seeded2, CancellationToken.None);
        var seeded3 = CreatePrincipal("principal-3");
        await Store.AddAsync(seeded3, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var loaded = await Store.GetByIdsAsync(
            ["principal-1", "principal-3", "missing"],
            CancellationToken.None
        );

        var ids = loaded.Select(principal => principal.PrincipalId).Order().ToList();
        Assert.Equal(["principal-1", "principal-3"], ids);
    }

    [Fact]
    public async Task GetByIdsAsync_WhenTheIdentifiersDifferInCase_MatchesThem()
    {
        var seeded1 = CreatePrincipal("Principal-1");
        await Store.AddAsync(seeded1, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var loaded = await Store.GetByIdsAsync(["principal-1"], CancellationToken.None);

        var principal = Assert.Single(loaded);
        Assert.Equal("Principal-1", principal.PrincipalId);
    }

    [Fact]
    public async Task GetByIdsAsync_WhenNoIdentifiers_ReturnsNone()
    {
        var loaded = await Store.GetByIdsAsync([], CancellationToken.None);

        Assert.Empty(loaded);
    }

    #endregion

    #region GetExistingIdsAsync Tests

    [Fact]
    public async Task GetExistingIdsAsync_WhenSomeIdentifiersMatch_ReturnsOnlyTheirIdentifiers()
    {
        var seeded1 = CreatePrincipal("principal-1");
        await Store.AddAsync(seeded1, CancellationToken.None);
        var seeded2 = CreatePrincipal("principal-2");
        await Store.AddAsync(seeded2, CancellationToken.None);
        var seeded3 = CreatePrincipal("principal-3");
        await Store.AddAsync(seeded3, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var principalIds = new[] { "principal-1", "principal-3", "missing" };

        var existing = await Store.GetExistingIdsAsync(principalIds, CancellationToken.None);

        var ids = existing.Order().ToList();
        Assert.Equal(["principal-1", "principal-3"], ids);
    }

    [Fact]
    public async Task GetExistingIdsAsync_WhenTheIdentifiersDifferInCase_ReturnsTheStoredIdentifier()
    {
        var seeded1 = CreatePrincipal("Principal-1");
        await Store.AddAsync(seeded1, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var principalIds = new[] { "principal-1" };

        var existing = await Store.GetExistingIdsAsync(principalIds, CancellationToken.None);

        var id = Assert.Single(existing);
        Assert.Equal("Principal-1", id);
    }

    [Fact]
    public async Task GetExistingIdsAsync_WhenNoIdentifiers_ReturnsNone()
    {
        var principalIds = Array.Empty<string>();

        var existing = await Store.GetExistingIdsAsync(principalIds, CancellationToken.None);

        Assert.Empty(existing);
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_RemovesTheConsentsAndGrantsOfThePrincipal()
    {
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        const string clientId = "client-1";
        await Store.AddAsync(CreatePrincipal("principal-1"), CancellationToken.None);
        await Store.AddAsync(CreatePrincipal("principal-2"), CancellationToken.None);
        var clientStore = new ClientStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext,
            TestCryptoServices.CreateResourceIdGenerator()
        );
        await clientStore.AddAsync(CreateClient(clientId), CancellationToken.None);
        await DbContext.SaveChangesAsync();
        var consentStore = new ConsentStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
        await consentStore.SaveAsync(
            CreateConsent(clientId, "principal-1"),
            CancellationToken.None
        );
        await consentStore.SaveAsync(
            CreateConsent(clientId, "principal-2"),
            CancellationToken.None
        );
        var grantStore = new GrantStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
        await grantStore.AddAsync(CreateGrant(clientId, "principal-1"), CancellationToken.None);
        await grantStore.AddAsync(CreateGrant(clientId, "principal-2"), CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await Store.RemoveAsync("principal-1", CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var remainingConsents = await DbContext
            .Consents.IgnoreQueryFilters()
            .Select(entity => entity.SubjectId)
            .ToListAsync();
        var remainingGrants = await DbContext
            .Grants.IgnoreQueryFilters()
            .Select(entity => entity.SubjectId)
            .ToListAsync();
        Assert.Equal(["principal-2"], remainingConsents);
        Assert.Equal(["principal-2"], remainingGrants);
    }

    #endregion
}
