// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.GlobalDirectory;

// Uses SQLite because the EF InMemory provider does not enforce concurrency tokens.
public sealed class ReplicatedDocumentOutboxInterceptorTests : IDisposable
{
    private const string ControlPlaneTenantId = "control-plane";

    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }
    private TestOutboxPolicy Policy { get; } = new();
    private CountingNotifier Notifier { get; } = new();
    private SettableAmbientTenantAccessor Accessor { get; } = new();

    private T ReadContent<T>(string contentJson) =>
        Provider
            .GetRequiredService<IPersistedPayloadSerializer>()
            .Deserialize<T>(
                JsonElement.Parse(contentJson),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            );

    public ReplicatedDocumentOutboxInterceptorTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IDirectoryOutboxPolicy>(Policy);
        serviceCollection.AddSingleton<IDirectoryOutboxNotifier>(Notifier);
        serviceCollection.AddSingleton<IAmbientTenantAccessor>(Accessor);
        serviceCollection.AddTestOpenIdDbContext(options => options.UseSqlite(Connection));
        Provider = serviceCollection.BuildServiceProvider();

        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        context.Database.EnsureCreated();
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        context.SeedTenant(keyGenerator, ControlPlaneTenantId);
    }

    public void Dispose()
    {
        Provider.Dispose();
        Connection.Dispose();
    }

    #region Helpers

    private sealed class TestOutboxPolicy : IDirectoryOutboxPolicy
    {
        public bool IsPublishing { get; set; }

        public string ControlPlaneTenantId =>
            ReplicatedDocumentOutboxInterceptorTests.ControlPlaneTenantId;
    }

    private sealed class SettableAmbientTenantAccessor : IAmbientTenantAccessor
    {
        public string? TenantId { get; set; }

        public bool IsScoped => TenantId is not null;

        public bool IsUnscoped { get; set; } = true;

        public string? UnscopedReason => IsUnscoped ? "test" : null;

        public IDisposable BeginScope(string tenantId) => throw new NotSupportedException();

        public IDisposable BeginUnscoped(string reason) => throw new NotSupportedException();
    }

    private sealed class CountingNotifier : IDirectoryOutboxNotifier
    {
        public int Count { get; private set; }

        public void Notify() => Count++;
    }

    private async ValueTask<T> WithContextAsync<T>(
        Func<OpenIdDbContext, ISurrogateKeyGenerator, ValueTask<T>> action,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        cancellationToken.ThrowIfCancellationRequested();
        return await action(context, keyGenerator);
    }

    private static RoleAssignmentEntity CreateAssignment(
        ISurrogateKeyGenerator keyGenerator,
        TenantEntity tenant,
        string principalId,
        string roleName,
        string resourceType,
        string resourceId
    ) =>
        new()
        {
            Id = keyGenerator.CreateKey(),
            TenantId = tenant.Id,
            NormalizedTenantId = tenant.NormalizedTenantId,
            AssignmentId = Guid.NewGuid().ToString("N"),
            NormalizedAssignmentId = Guid.NewGuid().ToString("N"),
            PrincipalId = principalId,
            NormalizedPrincipalId = principalId.ToLowerInvariant(),
            RoleName = roleName,
            NormalizedRoleName = roleName.ToLowerInvariant(),
            ResourceType = resourceType,
            ResourceId = resourceId,
            NormalizedResourceId = resourceId.ToLowerInvariant(),
            ConcurrencyToken = string.Empty,
            Tenant = tenant,
        };

    private static TenantSecretEntity CreateSecret(
        ISurrogateKeyGenerator keyGenerator,
        TenantEntity tenant,
        string keyId
    )
    {
        var secret = new SecretEntity
        {
            Id = keyGenerator.CreateKey(),
            SecretId = keyId,
            NormalizedSecretId = keyId,
            KeyId = keyId,
            ConcurrencyToken = string.Empty,
            Use = "sig",
            Algorithm = "ES256",
            CreatedWhen = DateTimeOffset.UnixEpoch,
            ActivatesWhen = DateTimeOffset.UnixEpoch,
            RetiresWhen = DateTimeOffset.UnixEpoch.AddYears(1),
            ExpiresWhen = DateTimeOffset.UnixEpoch.AddYears(2),
            SecretType = SecretTypes.Ecc,
            SecretEncoding = SecretEncodings.Pkcs8,
            Origin = SecretOrigins.Generated,
            ManagedBy = null,
            KeySizeBits = 256,
            EncodedValue = "encoded-value",
            PublicKey = "public-material",
        };

        return new TenantSecretEntity
        {
            Id = keyGenerator.CreateKey(),
            TenantId = tenant.Id,
            NormalizedTenantId = tenant.NormalizedTenantId,
            SecretId = secret.Id,
            Tenant = tenant,
            Secret = secret,
        };
    }

    private async ValueTask<List<DirectoryOutboxEntity>> ListOutboxAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        return await context
            .Set<DirectoryOutboxEntity>()
            .AsNoTracking()
            .OrderBy(entity => entity.Sequence)
            .ToListAsync(cancellationToken);
    }

    private async ValueTask<List<ReplicatedSourceRevisionEntity>> ListRevisionsAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var scope = Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenIdDbContext>();
        return await context
            .Set<ReplicatedSourceRevisionEntity>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private async ValueTask GrantAtPlatformAsync(
        string principalId,
        CancellationToken cancellationToken = default
    )
    {
        await WithContextAsync(
            async (context, keyGenerator) =>
            {
                var tenant = await context
                    .Set<TenantEntity>()
                    .SingleAsync(entity => entity.NormalizedTenantId == ControlPlaneTenantId);
                var assignment = CreateAssignment(
                    keyGenerator,
                    tenant,
                    principalId,
                    "GlobalAdmin",
                    OpenIdResourceTypes.Platform,
                    OpenIdResourceIds.Platform
                );
                context.Set<RoleAssignmentEntity>().Add(assignment);
                await context.SaveChangesAsync();
                return 0;
            },
            cancellationToken
        );
    }

    #endregion

    [Fact]
    public async Task SaveChanges_WhenNotPublishing_WritesNoOutbox()
    {
        Policy.IsPublishing = false;

        await GrantAtPlatformAsync("principal-a");

        Assert.Empty(await ListOutboxAsync());
        Assert.Empty(await ListRevisionsAsync());
        Assert.Equal(0, Notifier.Count);
    }

    [Fact]
    public async Task SaveChanges_WhenAssignmentGrantedAtPlatform_WritesThePlatformAuthority()
    {
        Policy.IsPublishing = true;

        await GrantAtPlatformAsync("principal-a");

        var outbox = Assert.Single(await ListOutboxAsync());
        Assert.Equal(ReplicatedDocumentKinds.PlatformAuthority, outbox.Kind);
        Assert.Equal(string.Empty, outbox.Key);
        Assert.Equal(1, outbox.Revision);
        var content = ReadContent<PlatformAuthorityDocumentContent>(outbox.ContentJson!);
        var assignment = Assert.Single(content!.Assignments);
        Assert.Equal("principal-a", assignment.PrincipalId);
        Assert.Equal("GlobalAdmin", assignment.RoleName);
        var revision = Assert.Single(await ListRevisionsAsync());
        Assert.Equal(1, revision.Revision);
        Assert.Equal(1, Notifier.Count);
    }

    [Fact]
    public async Task SaveChanges_WhenInTheControlPlaneTenantScope_WritesTheOutbox()
    {
        Policy.IsPublishing = true;
        Accessor.IsUnscoped = false;
        Accessor.TenantId = ControlPlaneTenantId;

        await GrantAtPlatformAsync("principal-a");

        var outbox = Assert.Single(await ListOutboxAsync());
        Assert.Equal(ReplicatedDocumentKinds.PlatformAuthority, outbox.Kind);
    }

    [Fact]
    public async Task SaveChanges_WhenSavedAgain_RaisesTheRevisionByOne()
    {
        Policy.IsPublishing = true;

        await GrantAtPlatformAsync("principal-a");
        await GrantAtPlatformAsync("principal-b");

        var outbox = await ListOutboxAsync();
        Assert.Equal([1L, 2L], outbox.Select(entry => entry.Revision).ToArray());
        var content = ReadContent<PlatformAuthorityDocumentContent>(outbox[1].ContentJson!);
        Assert.Equal(2, content!.Assignments.Count);
        var revision = Assert.Single(await ListRevisionsAsync());
        Assert.Equal(2, revision.Revision);
        Assert.Equal(2, Notifier.Count);
    }

    [Fact]
    public async Task SaveChanges_WhenTeamCreated_WritesTheTeamAuthority()
    {
        Policy.IsPublishing = true;

        await WithContextAsync(
            (context, keyGenerator) =>
            {
                context.SeedTeam(keyGenerator, "team-a");
                return ValueTask.FromResult(0);
            }
        );

        var outbox = await ListOutboxAsync();
        var entry = Assert.Single(outbox, item => item.Key == "team-a");
        Assert.Equal(ReplicatedDocumentKinds.TeamAuthority, entry.Kind);
        Assert.NotNull(entry.ContentJson);
    }

    [Fact]
    public async Task SaveChanges_WhenTeamRetired_WritesARetiredTeamAuthority()
    {
        await WithContextAsync(
            (context, keyGenerator) =>
            {
                context.SeedTeam(keyGenerator, "team-b");
                return ValueTask.FromResult(0);
            }
        );
        Policy.IsPublishing = true;

        await WithContextAsync(
            async (context, keyGenerator) =>
            {
                var team = await context
                    .Set<TeamEntity>()
                    .SingleAsync(entity => entity.NormalizedTeamId == "team-b");
                context.Set<TeamEntity>().Remove(team);
                var retired = new RetiredTeamEntity
                {
                    Id = keyGenerator.CreateKey(),
                    TeamId = "team-b",
                    NormalizedTeamId = "team-b",
                };
                context.Set<RetiredTeamEntity>().Add(retired);
                await context.SaveChangesAsync();
                return 0;
            }
        );

        var entry = Assert.Single(await ListOutboxAsync());
        Assert.Equal(ReplicatedDocumentKinds.TeamAuthority, entry.Kind);
        Assert.Equal("team-b", entry.Key);
        Assert.Null(entry.ContentJson);
    }

    [Fact]
    public async Task SaveChanges_WhenControlPlaneSecretAdded_WritesTheManagementKeys()
    {
        Policy.IsPublishing = true;

        await WithContextAsync(
            async (context, keyGenerator) =>
            {
                var tenant = await context
                    .Set<TenantEntity>()
                    .SingleAsync(entity => entity.NormalizedTenantId == ControlPlaneTenantId);
                var tenantSecret = CreateSecret(keyGenerator, tenant, "kid-1");
                context.Set<TenantSecretEntity>().Add(tenantSecret);
                await context.SaveChangesAsync();
                return 0;
            }
        );

        var entry = Assert.Single(await ListOutboxAsync());
        Assert.Equal(ReplicatedDocumentKinds.ManagementKeys, entry.Kind);
        var content = ReadContent<ManagementKeysDocumentContent>(entry.ContentJson!);
        var key = Assert.Single(content!.Keys);
        Assert.Equal("kid-1", key.KeyId);
    }

    [Fact]
    public async Task SaveChanges_WhenRevokedKeyAdded_ListsItInTheManagementKeys()
    {
        Policy.IsPublishing = true;

        await WithContextAsync(
            async (context, _) =>
            {
                var revoked = new RevokedManagementKeyEntity
                {
                    KeyId = "kid-9",
                    RevokedWhen = DateTimeOffset.UnixEpoch,
                    Source = "control-plane",
                };
                context.Set<RevokedManagementKeyEntity>().Add(revoked);
                await context.SaveChangesAsync();
                return 0;
            }
        );

        var entry = Assert.Single(await ListOutboxAsync());
        var content = ReadContent<ManagementKeysDocumentContent>(entry.ContentJson!);
        Assert.Equal(["kid-9"], content!.RevokedKeyIds.ToArray());
    }
}
