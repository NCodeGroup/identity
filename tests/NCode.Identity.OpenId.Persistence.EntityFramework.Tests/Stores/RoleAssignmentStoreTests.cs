// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

public sealed class RoleAssignmentStoreTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string Subject = "subject-1";

    private SwitchableAmbientTenantAccessor Ambient { get; } = new();
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private TenantStore TenantStore { get; }
    private RoleAssignmentStore Store { get; }

    public RoleAssignmentStoreTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IAmbientTenantAccessor>(Ambient);
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        TenantStore = new TenantStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
        Store = new RoleAssignmentStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
    }

    private static PersistedRoleAssignment CreateAssignment(
        string assignmentId,
        string principalId,
        string roleName,
        string resourceType,
        string resourceId
    ) =>
        new()
        {
            TenantId = TenantId,
            AssignmentId = assignmentId,
            PrincipalId = principalId,
            RoleName = roleName,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ConcurrencyToken = string.Empty,
        };

    private async ValueTask SeedTenantAsync(CancellationToken cancellationToken)
    {
        DbContext.SeedTeam(Provider.GetRequiredService<ISurrogateKeyGenerator>(), "platform");
        var tenant = new PersistedTenantBuilder
        {
            TenantId = TenantId,
            DisplayName = "Tenant One",
        }.Build();
        await TenantStore.AddAsync(tenant, cancellationToken);

        // The tenant must be persisted before its dependents are added (an in-memory query does not observe
        // unsaved inserts).
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    [Fact]
    public async Task AddAsync_WhenPersisted_RoundTripsByAssignmentId()
    {
        await SeedTenantAsync(CancellationToken.None);

        using var scope = Ambient.BeginScope(TenantId);

        await Store.AddAsync(
            CreateAssignment("assign-1", Subject, "Owner", "client", "client-1"),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetOrDefaultAsync("assign-1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(TenantId, loaded.TenantId);
        Assert.Equal(Subject, loaded.PrincipalId);
        Assert.Equal("Owner", loaded.RoleName);
        Assert.Equal("client", loaded.ResourceType);
        Assert.Equal("client-1", loaded.ResourceId);
        Assert.NotEmpty(loaded.ConcurrencyToken);
    }

    [Fact]
    public async Task GetByResourceAsync_WhenNodeHasOwners_ReturnsOnlyThatNode()
    {
        await SeedTenantAsync(CancellationToken.None);

        using var scope = Ambient.BeginScope(TenantId);

        await Store.AddAsync(
            CreateAssignment("assign-1", "owner-a", "Owner", "client", "client-1"),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateAssignment("assign-2", "owner-b", "Owner", "client", "client-1"),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateAssignment("assign-3", "owner-c", "Owner", "client", "client-2"),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var atNode = await Store.GetByResourceAsync("client", "client-1", CancellationToken.None);

        Assert.Equal(2, atNode.Count);
        Assert.All(atNode, assignment => Assert.Equal("client-1", assignment.ResourceId));
    }

    [Fact]
    public async Task GetByPrincipalAsync_ReturnsEveryAssignmentForThePrincipal()
    {
        await SeedTenantAsync(CancellationToken.None);

        await Store.AddAsync(
            CreateAssignment("assign-1", Subject, "Owner", "client", "client-1"),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateAssignment("assign-2", Subject, "TenantAdmin", "tenant", TenantId),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateAssignment("assign-3", "other", "Owner", "client", "client-3"),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        using var scope = Ambient.BeginUnscoped("test");
        var forPrincipal = await Store.GetByPrincipalAsync(Subject, CancellationToken.None);

        Assert.Equal(2, forPrincipal.Count);
        Assert.All(forPrincipal, assignment => Assert.Equal(Subject, assignment.PrincipalId));
    }

    // See ADR-1411
    [Fact]
    public async Task GetByPrincipalAsync_WhenUnscoped_ReadsAssignmentsOfEveryTenant()
    {
        await SeedTenantAsync(CancellationToken.None);
        DbContext.SeedTenant(Provider.GetRequiredService<ISurrogateKeyGenerator>(), "tenant-2");

        await Store.AddAsync(
            CreateAssignment("assign-1", Subject, "Owner", "client", "client-1"),
            CancellationToken.None
        );
        await Store.AddAsync(
            new PersistedRoleAssignment
            {
                TenantId = "tenant-2",
                AssignmentId = "assign-2",
                PrincipalId = Subject,
                RoleName = "TeamAdmin",
                ResourceType = "team",
                ResourceId = "platform",
                ConcurrencyToken = string.Empty,
            },
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        using var scope = Ambient.BeginUnscoped("test");
        var forPrincipal = await Store.GetByPrincipalAsync(Subject, CancellationToken.None);

        Assert.Equal(2, forPrincipal.Count);
        Assert.Contains(forPrincipal, assignment => assignment.TenantId == TenantId);
        Assert.Contains(forPrincipal, assignment => assignment.TenantId == "tenant-2");
    }

    [Fact]
    public async Task GetByPrincipalAsync_WhenTheScopeIsATenantScope_Throws()
    {
        await SeedTenantAsync(CancellationToken.None);

        using var scope = Ambient.BeginScope(TenantId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.GetByPrincipalAsync(Subject, CancellationToken.None)
        );

        Assert.Equal(
            "RoleAssignmentStore.GetByPrincipalAsync reads across tenants and runs only in an unscoped scope (IAmbientTenantAccessor.BeginUnscoped).",
            exception.Message
        );
    }

    [Fact]
    public async Task RemoveAsync_WhenAssignmentExists_RemovesIt()
    {
        await SeedTenantAsync(CancellationToken.None);

        using var scope = Ambient.BeginScope(TenantId);

        await Store.AddAsync(
            CreateAssignment("assign-1", Subject, "Owner", "client", "client-1"),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        await Store.RemoveAsync("assign-1", CancellationToken.None);
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetOrDefaultAsync("assign-1", CancellationToken.None);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task RemoveByPrincipalAsync_WhenUnscoped_RemovesTheAssignmentsOfThePrincipalInEveryTenant()
    {
        await SeedTenantAsync(CancellationToken.None);
        DbContext.SeedTenant(Provider.GetRequiredService<ISurrogateKeyGenerator>(), "tenant-2");

        using var scope = Ambient.BeginUnscoped("test");

        await Store.AddAsync(
            CreateAssignment("assign-1", Subject, "Owner", "client", "client-1"),
            CancellationToken.None
        );
        await Store.AddAsync(
            new PersistedRoleAssignment
            {
                TenantId = "tenant-2",
                AssignmentId = "assign-2",
                PrincipalId = Subject,
                RoleName = "TeamAdmin",
                ResourceType = "team",
                ResourceId = "platform",
                ConcurrencyToken = string.Empty,
            },
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateAssignment("assign-3", "other", "Owner", "client", "client-3"),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await Store.RemoveByPrincipalAsync(Subject, CancellationToken.None);
        await DbContext.SaveChangesAsync();

        var remaining = await DbContext
            .RoleAssignments.Select(entity => entity.AssignmentId)
            .ToListAsync();
        Assert.Equal(["assign-3"], remaining);
    }

    [Fact]
    public async Task RemoveByPrincipalAsync_WhenTheScopeIsATenantScope_Throws()
    {
        await SeedTenantAsync(CancellationToken.None);

        using var scope = Ambient.BeginScope(TenantId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.RemoveByPrincipalAsync(Subject, CancellationToken.None)
        );

        Assert.Equal(
            "RoleAssignmentStore.RemoveByPrincipalAsync reads across tenants and runs only in an unscoped scope (IAmbientTenantAccessor.BeginUnscoped).",
            exception.Message
        );
    }
}

/// <summary>
/// An ambient tenant accessor whose tenant and unscoped scopes a test begins and ends, so the fail-closed
/// tenant-scoping query filter applies. With no scope begun, the scope is unset and matches no tenant row.
/// </summary>
internal sealed class SwitchableAmbientTenantAccessor : IAmbientTenantAccessor
{
    public string? TenantId { get; private set; }

    public bool IsScoped => TenantId is not null;

    public bool IsUnscoped { get; private set; }

    public string? UnscopedReason { get; private set; }

    public IDisposable BeginScope(string tenantId)
    {
        var scope = new Scope(this);
        TenantId = tenantId;
        IsUnscoped = false;
        UnscopedReason = null;
        return scope;
    }

    public IDisposable BeginUnscoped(string reason)
    {
        var scope = new Scope(this);
        TenantId = null;
        IsUnscoped = true;
        UnscopedReason = reason;
        return scope;
    }

    private sealed class Scope(SwitchableAmbientTenantAccessor accessor) : IDisposable
    {
        private string? PreviousTenantId { get; } = accessor.TenantId;
        private bool PreviousIsUnscoped { get; } = accessor.IsUnscoped;
        private string? PreviousUnscopedReason { get; } = accessor.UnscopedReason;

        public void Dispose()
        {
            accessor.TenantId = PreviousTenantId;
            accessor.IsUnscoped = PreviousIsUnscoped;
            accessor.UnscopedReason = PreviousUnscopedReason;
        }
    }
}
