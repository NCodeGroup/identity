// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using IdGen;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// Validates TenantStore.RemoveMovedAsync against a relational provider that enforces foreign keys (ADR-1514). The
// tenant lives on in another cell, so every row it owns here is removed and nothing is retired.
public sealed class TenantStoreMovedRemovalTests : IDisposable
{
    private const string TenantOneId = "tenant-1";
    private const string TenantTwoId = "tenant-2";

    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private TenantStore Store { get; }
    private ResourceServerStore ResourceServerStore { get; }
    private RoleAssignmentStore RoleAssignmentStore { get; }

    public TenantStoreMovedRemovalTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options => options.UseSqlite(Connection));
        Provider = serviceCollection.BuildServiceProvider();

        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        DbContext.Database.EnsureCreated();

        var idGenerator = Provider.GetRequiredService<IIdGenerator<long>>();
        DbContext.SeedTeam(idGenerator, "platform");

        Store = new TenantStore(Mock.Of<IStoreProvider>(), idGenerator, DbContext);
        ResourceServerStore = new ResourceServerStore(
            Mock.Of<IStoreProvider>(),
            idGenerator,
            DbContext
        );
        RoleAssignmentStore = new RoleAssignmentStore(
            Mock.Of<IStoreProvider>(),
            idGenerator,
            DbContext
        );
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
        Connection.Dispose();
    }

    #region Helpers

    private sealed record RowCounts(
        int Tenants,
        int TenantSecrets,
        int Secrets,
        int ResourceServers,
        int Scopes,
        int ResourceServerSecrets,
        int RoleAssignments,
        int Grants,
        int FederatedPrincipals,
        int FederatedIdentities,
        int MasterKeys,
        int OwnerRootKeys
    );

    private static PersistedSecret CreateSecret(string secretId) =>
        new PersistedSecretBuilder
        {
            SecretId = secretId,
            Use = "sig",
            Algorithm = "RS256",
            EncodedValue = "encoded-value",
        }.Build();

    private async ValueTask SeedTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        await Store.AddAsync(
            new PersistedTenantBuilder
            {
                TenantId = tenantId,
                Secrets =
                [
                    CreateSecret($"{tenantId}-secret-1"),
                    CreateSecret($"{tenantId}-secret-2"),
                ],
            }.Build(),
            cancellationToken
        );
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    private async ValueTask SeedResourceServerAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var resourceServerId = $"system-{tenantId}";

        await ResourceServerStore.AddAsync(
            new PersistedResourceServerBuilder
            {
                TenantId = tenantId,
                ResourceServerId = resourceServerId,
                Identifier = $"urn:ncode:openid:{tenantId}",
                Name = resourceServerId,
                IsSystem = true,
                Scopes =
                [
                    new PersistedScope
                    {
                        Value = "openid",
                        Description = "Sign you in and issue an identity token.",
                        IsSystem = true,
                    },
                    new PersistedScope
                    {
                        Value = "profile",
                        Description = "Read your profile.",
                        IsSystem = true,
                    },
                ],
                Secrets = [CreateSecret($"{resourceServerId}-secret")],
            }.Build(),
            cancellationToken
        );
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    private async ValueTask SeedRoleAssignmentAsync(
        string assignmentId,
        string tenantId,
        string principalId,
        CancellationToken cancellationToken
    )
    {
        await RoleAssignmentStore.AddAsync(
            new PersistedRoleAssignment
            {
                TenantId = tenantId,
                AssignmentId = assignmentId,
                PrincipalId = principalId,
                RoleName = "Owner",
                ResourceType = "tenant",
                ResourceId = tenantId,
                ConcurrencyToken = string.Empty,
            },
            cancellationToken
        );
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    private async ValueTask SeedEndUserDataAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var idGenerator = Provider.GetRequiredService<IIdGenerator<long>>();
        var grantStore = new GrantStore(Mock.Of<IStoreProvider>(), idGenerator, DbContext);
        var principalStore = new FederatedPrincipalStore(
            Mock.Of<IStoreProvider>(),
            idGenerator,
            DbContext
        );
        var identityStore = new FederatedIdentityStore(
            Mock.Of<IStoreProvider>(),
            idGenerator,
            DbContext
        );

        await grantStore.AddAsync(
            new PersistedGrant
            {
                GrantType = "session",
                HashedKey = $"{tenantId}-session",
                GrantId = $"{tenantId}-session",
                ConcurrencyToken = string.Empty,
                TenantId = tenantId,
                ClientId = null,
                SubjectId = "subject-1",
                SessionId = null,
                RefreshTokenLineageId = null,
                CreatedWhen = DateTimeOffset.UnixEpoch,
                ExpiresWhen = DateTimeOffset.UnixEpoch.AddHours(1),
                RevokedWhen = null,
                ConsumedWhen = null,
                ProtectedPayload = "protected-payload",
            },
            cancellationToken
        );
        await DbContext.SaveChangesAsync(cancellationToken);

        await principalStore.AddAsync(
            new PersistedFederatedPrincipal
            {
                TenantId = tenantId,
                PrincipalId = $"{tenantId}-principal",
                ConcurrencyToken = string.Empty,
            },
            cancellationToken
        );
        await DbContext.SaveChangesAsync(cancellationToken);

        await identityStore.AddAsync(
            new PersistedFederatedIdentity
            {
                TenantId = tenantId,
                FederatedIdentityId = $"{tenantId}-identity",
                PrincipalId = $"{tenantId}-principal",
                Issuer = "https://issuer.example",
                Subject = $"{tenantId}-subject",
                JoinKey = null,
                ConcurrencyToken = string.Empty,
            },
            cancellationToken
        );
        await DbContext.SaveChangesAsync(cancellationToken);

        DbContext.ChangeTracker.Clear();
    }

    private async ValueTask SeedKeyRowsAsync(string tenantId, CancellationToken cancellationToken)
    {
        var idGenerator = Provider.GetRequiredService<IIdGenerator<long>>();

        DbContext
            .Set<MasterKeyEntity>()
            .Add(
                new MasterKeyEntity
                {
                    Id = idGenerator.CreateId(),
                    KeyId = Guid.NewGuid(),
                    OwnerType = SecretOwnerTypes.Tenant,
                    OwnerId = tenantId,
                    NormalizedOwnerId = tenantId,
                    Version = 1,
                    CreatedWhen = DateTimeOffset.UnixEpoch,
                    RetiredWhen = null,
                    RootKeyProvider = "test",
                    RootKeyId = "root-key",
                    RootKeyReference = null,
                    RootKeyAccessIdentity = null,
                    RootWrappedKey = "wrapped-key",
                    RecoveryKeyId = null,
                    RecoveryWrappedKey = null,
                    KeyCheck = "key-check",
                    ConcurrencyToken = string.Empty,
                }
            );
        DbContext
            .Set<OwnerRootKeyEntity>()
            .Add(
                new OwnerRootKeyEntity
                {
                    Id = idGenerator.CreateId(),
                    OwnerType = SecretOwnerTypes.Tenant,
                    OwnerId = tenantId,
                    NormalizedOwnerId = tenantId,
                    KeyWrapperName = "test",
                    KeyReference = "key-reference",
                    AccessIdentity = null,
                    State = "active",
                    CreatedWhen = DateTimeOffset.UnixEpoch,
                    StateChangedWhen = DateTimeOffset.UnixEpoch,
                    ValidatedWhen = null,
                    FailedWhen = null,
                    Failure = null,
                    ConcurrencyToken = string.Empty,
                }
            );
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    private async ValueTask SeedFullTenantAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        await SeedTenantAsync(tenantId, cancellationToken);
        await SeedResourceServerAsync(tenantId, cancellationToken);
        await SeedRoleAssignmentAsync(
            $"assign-{tenantId}",
            tenantId,
            "subject-1",
            cancellationToken
        );
        await SeedEndUserDataAsync(tenantId, cancellationToken);
        await SeedKeyRowsAsync(tenantId, cancellationToken);
    }

    private async ValueTask<RowCounts> CountRowsAsync(CancellationToken cancellationToken)
    {
        DbContext.ChangeTracker.Clear();

        return new RowCounts(
            Tenants: await DbContext.Set<TenantEntity>().CountAsync(cancellationToken),
            TenantSecrets: await DbContext
                .Set<TenantSecretEntity>()
                .IgnoreQueryFilters()
                .CountAsync(cancellationToken),
            Secrets: await DbContext.Set<SecretEntity>().CountAsync(cancellationToken),
            ResourceServers: await DbContext
                .Set<ResourceServerEntity>()
                .IgnoreQueryFilters()
                .CountAsync(cancellationToken),
            Scopes: await DbContext
                .Set<ScopeEntity>()
                .IgnoreQueryFilters()
                .CountAsync(cancellationToken),
            ResourceServerSecrets: await DbContext
                .Set<ResourceServerSecretEntity>()
                .IgnoreQueryFilters()
                .CountAsync(cancellationToken),
            RoleAssignments: await DbContext
                .Set<RoleAssignmentEntity>()
                .IgnoreQueryFilters()
                .CountAsync(cancellationToken),
            Grants: await DbContext
                .Set<GrantEntity>()
                .IgnoreQueryFilters()
                .CountAsync(cancellationToken),
            FederatedPrincipals: await DbContext
                .Set<FederatedPrincipalEntity>()
                .IgnoreQueryFilters()
                .CountAsync(cancellationToken),
            FederatedIdentities: await DbContext
                .Set<FederatedIdentityEntity>()
                .IgnoreQueryFilters()
                .CountAsync(cancellationToken),
            MasterKeys: await DbContext.Set<MasterKeyEntity>().CountAsync(cancellationToken),
            OwnerRootKeys: await DbContext.Set<OwnerRootKeyEntity>().CountAsync(cancellationToken)
        );
    }

    #endregion

    #region RemoveMovedAsync Tests

    [Fact]
    public async Task RemoveMovedAsync_WhenTheTenantExists_RemovesEveryRowItOwnsAndNoOtherTenantsRows()
    {
        await SeedFullTenantAsync(TenantOneId, CancellationToken.None);
        await SeedFullTenantAsync(TenantTwoId, CancellationToken.None);

        Assert.Equal(
            new RowCounts(2, 4, 6, 2, 4, 2, 2, 2, 2, 2, 2, 2),
            await CountRowsAsync(CancellationToken.None)
        );

        var removed = await Store.RemoveMovedAsync(TenantOneId, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        Assert.True(removed);
        Assert.Equal(
            new RowCounts(1, 2, 3, 1, 2, 1, 1, 1, 1, 1, 1, 1),
            await CountRowsAsync(CancellationToken.None)
        );

        var tenant = Assert.Single(await DbContext.Set<TenantEntity>().ToListAsync());
        Assert.Equal(TenantTwoId, tenant.TenantId);
        Assert.All(
            await DbContext.Set<MasterKeyEntity>().ToListAsync(),
            entity => Assert.Equal(TenantTwoId, entity.NormalizedOwnerId)
        );
        Assert.All(
            await DbContext.Set<OwnerRootKeyEntity>().ToListAsync(),
            entity => Assert.Equal(TenantTwoId, entity.NormalizedOwnerId)
        );
        Assert.All(
            await DbContext.Set<SecretEntity>().ToListAsync(),
            entity => Assert.Contains(TenantTwoId, entity.SecretId)
        );
    }

    [Fact]
    public async Task RemoveMovedAsync_WhenTheTenantHasAResourceServer_DoesNotCheckDependents()
    {
        await SeedFullTenantAsync(TenantOneId, CancellationToken.None);
        await ResourceServerStore.AddAsync(
            new PersistedResourceServerBuilder
            {
                TenantId = TenantOneId,
                ResourceServerId = "api-tenant-1",
                Identifier = "https://api.example.com/tenant-1",
                Name = "api-tenant-1",
                IsSystem = false,
            }.Build(),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        Assert.True(await Store.HasDependentsAsync(TenantOneId, CancellationToken.None));

        var removed = await Store.RemoveMovedAsync(TenantOneId, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        Assert.True(removed);
        Assert.Empty(await DbContext.Set<TenantEntity>().ToListAsync());
        Assert.Empty(
            await DbContext.Set<ResourceServerEntity>().IgnoreQueryFilters().ToListAsync()
        );
    }

    [Fact]
    public async Task RemoveMovedAsync_WhenTheTenantHasAnIssuer_WritesNoRetiredRow()
    {
        await Store.AddAsync(
            new PersistedTenantBuilder
            {
                TenantId = TenantOneId,
                Issuer = "https://op.example/tenant-1",
            }.Build(),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var removed = await Store.RemoveMovedAsync(TenantOneId, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        Assert.True(removed);
        Assert.Empty(await DbContext.Set<TenantEntity>().ToListAsync());
        Assert.Empty(await DbContext.Set<RetiredTenantEntity>().ToListAsync());
        Assert.Empty(await DbContext.Set<RetiredIssuerEntity>().ToListAsync());
    }

    [Fact]
    public async Task RemoveMovedAsync_WhenTheTenantDoesNotExist_ReturnsFalse()
    {
        await SeedFullTenantAsync(TenantTwoId, CancellationToken.None);
        var before = await CountRowsAsync(CancellationToken.None);

        var removed = await Store.RemoveMovedAsync(TenantOneId, CancellationToken.None);
        await DbContext.SaveChangesAsync();

        Assert.False(removed);
        Assert.Equal(before, await CountRowsAsync(CancellationToken.None));
    }

    #endregion
}
