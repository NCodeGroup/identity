// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

public sealed class FederatedIdentityStoreTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string PrincipalId = "principal-1";
    private const string Issuer = "https://issuer.example";
    private const string Subject = "upstream-subject-1";

    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private FederatedPrincipalStore PrincipalStore { get; }
    private FederatedIdentityStore Store { get; }

    public FederatedIdentityStoreTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        PrincipalStore = new FederatedPrincipalStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
        Store = new FederatedIdentityStore(
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

    private static PersistedFederatedIdentity CreateIdentity(
        string federatedIdentityId,
        string principalId,
        string issuer,
        string subject,
        string? joinKey = null
    ) =>
        new()
        {
            TenantId = TenantId,
            FederatedIdentityId = federatedIdentityId,
            PrincipalId = principalId,
            Issuer = issuer,
            Subject = subject,
            JoinKey = joinKey,
            ConcurrencyToken = string.Empty,
        };

    private async ValueTask SeedPrincipalAsync(
        string principalId,
        CancellationToken cancellationToken
    )
    {
        await PrincipalStore.AddAsync(
            new PersistedFederatedPrincipal
            {
                TenantId = TenantId,
                PrincipalId = principalId,
                ConcurrencyToken = string.Empty,
            },
            cancellationToken
        );

        // The principal must be persisted before its identities are added (an in-memory query does not observe
        // unsaved inserts).
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    [Fact]
    public async Task AddAsync_WhenPersisted_RoundTripsByFederatedIdentityId()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);

        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, Issuer, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetOrDefaultAsync("identity-1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("identity-1", loaded.FederatedIdentityId);
        Assert.Equal(PrincipalId, loaded.PrincipalId);
        Assert.Equal(Issuer, loaded.Issuer);
        Assert.Equal(Subject, loaded.Subject);
        Assert.NotEmpty(loaded.ConcurrencyToken);
    }

    [Fact]
    public async Task AddAsync_WhenPrincipalMissing_Throws()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.AddAsync(
                CreateIdentity("identity-1", "missing-principal", Issuer, Subject),
                CancellationToken.None
            )
        );

        Assert.Contains("missing-principal", exception.Message);
    }

    [Fact]
    public async Task GetByIssuerSubjectAsync_WhenMatch_ResolvesToOwningPrincipal()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, Issuer, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByIssuerSubjectAsync(Issuer, Subject, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(PrincipalId, loaded.PrincipalId);
        Assert.Equal("identity-1", loaded.FederatedIdentityId);
    }

    [Fact]
    public async Task GetByIssuerSubjectAsync_WhenNoMatch_ReturnsNull()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);

        var loaded = await Store.GetByIssuerSubjectAsync(Issuer, Subject, CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetByIssuerSubjectsAsync_WhenSomeSubjectsMatch_ReturnsOnlyTheMatchingIdentitiesOfTheIssuer()
    {
        const string secondPrincipalId = "principal-2";
        const string secondSubject = "upstream-subject-2";
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await SeedPrincipalAsync(secondPrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, Issuer, Subject),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateIdentity("identity-2", secondPrincipalId, Issuer, secondSubject),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateIdentity("identity-3", PrincipalId, "https://other.example", Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByIssuerSubjectsAsync(
            Issuer,
            [Subject, secondSubject, "missing"],
            CancellationToken.None
        );

        Assert.Equal(2, loaded.Count);
        Assert.Equal(
            PrincipalId,
            Assert
                .Single(loaded, identity => identity.FederatedIdentityId == "identity-1")
                .PrincipalId
        );
        Assert.Equal(
            secondPrincipalId,
            Assert
                .Single(loaded, identity => identity.FederatedIdentityId == "identity-2")
                .PrincipalId
        );
    }

    [Fact]
    public async Task GetByIssuerSubjectsAsync_WhenTheSubjectsDifferInCase_MatchesThem()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, Issuer, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByIssuerSubjectsAsync(
            Issuer,
            [Subject.ToUpperInvariant()],
            CancellationToken.None
        );

        var identity = Assert.Single(loaded);
        Assert.Equal(PrincipalId, identity.PrincipalId);
    }

    [Fact]
    public async Task GetByIssuerSubjectsAsync_WhenNoSubjects_ReturnsNone()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, Issuer, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByIssuerSubjectsAsync(Issuer, [], CancellationToken.None);

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task GetByPrincipalAsync_ReturnsAllIdentitiesOfThePrincipal()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, Issuer, Subject),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateIdentity("identity-2", PrincipalId, "https://other.example", "subject-2"),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByPrincipalAsync(PrincipalId, CancellationToken.None);

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, identity => identity.FederatedIdentityId == "identity-1");
        Assert.Contains(loaded, identity => identity.FederatedIdentityId == "identity-2");
        Assert.All(loaded, identity => Assert.Equal(PrincipalId, identity.PrincipalId));
    }

    #region GetByPrincipalsAsync Tests

    [Fact]
    public async Task GetByPrincipalsAsync_WhenSomePrincipalsHaveIdentities_ReturnsOnlyTheIdentitiesOfThosePrincipals()
    {
        const string secondPrincipalId = "principal-2";
        const string thirdPrincipalId = "principal-3";
        const string unrequestedPrincipalId = "principal-4";
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await SeedPrincipalAsync(secondPrincipalId, CancellationToken.None);
        await SeedPrincipalAsync(thirdPrincipalId, CancellationToken.None);
        await SeedPrincipalAsync(unrequestedPrincipalId, CancellationToken.None);
        var seeded1 = CreateIdentity("identity-1", PrincipalId, Issuer, Subject);
        await Store.AddAsync(seeded1, CancellationToken.None);
        var seeded2 = CreateIdentity(
            "identity-2",
            PrincipalId,
            "https://other.example",
            "subject-2"
        );
        await Store.AddAsync(seeded2, CancellationToken.None);
        var seeded3 = CreateIdentity("identity-3", secondPrincipalId, Issuer, "subject-3");
        await Store.AddAsync(seeded3, CancellationToken.None);
        var seeded4 = CreateIdentity("identity-4", unrequestedPrincipalId, Issuer, "subject-4");
        await Store.AddAsync(seeded4, CancellationToken.None);
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByPrincipalsAsync(
            [PrincipalId, secondPrincipalId, thirdPrincipalId],
            CancellationToken.None
        );

        Assert.Equal(3, loaded.Count);
        Assert.Contains(loaded, identity => identity.FederatedIdentityId == "identity-1");
        Assert.Contains(loaded, identity => identity.FederatedIdentityId == "identity-2");
        Assert.Contains(loaded, identity => identity.FederatedIdentityId == "identity-3");
        Assert.DoesNotContain(loaded, identity => identity.PrincipalId == unrequestedPrincipalId);
    }

    [Fact]
    public async Task GetByPrincipalsAsync_WhenTheIdentifiersDifferInCase_MatchesThem()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        var seeded1 = CreateIdentity("identity-1", PrincipalId, Issuer, Subject);
        await Store.AddAsync(seeded1, CancellationToken.None);
        await DbContext.SaveChangesAsync();

        var upperCasePrincipalId = PrincipalId.ToUpperInvariant();
        var loaded = await Store.GetByPrincipalsAsync(
            [upperCasePrincipalId],
            CancellationToken.None
        );

        var identity = Assert.Single(loaded);
        Assert.Equal(PrincipalId, identity.PrincipalId);
    }

    [Fact]
    public async Task GetByPrincipalsAsync_WhenNoPrincipals_ReturnsNone()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        var seeded1 = CreateIdentity("identity-1", PrincipalId, Issuer, Subject);
        await Store.AddAsync(seeded1, CancellationToken.None);
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByPrincipalsAsync([], CancellationToken.None);

        Assert.Empty(loaded);
    }

    #endregion

    [Fact]
    public async Task GetByJoinKeyAsync_WhenMatch_ReturnsIdentitiesWithThatJoinKey()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, Issuer, Subject, "user@example.com"),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateIdentity(
                "identity-2",
                PrincipalId,
                "https://other.example",
                "subject-2",
                "other@example.com"
            ),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByJoinKeyAsync("USER@example.com", CancellationToken.None);

        Assert.Single(loaded);
        Assert.Equal("identity-1", loaded[0].FederatedIdentityId);
        Assert.Equal("user@example.com", loaded[0].JoinKey);
    }
}
