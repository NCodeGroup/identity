// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Accounts;

/// <summary>
/// Shared test setup for the <see cref="OpenIdDbContext"/> over the local-account slice: registers the sequential
/// surrogate key generator and the test page cursor protector, the core persistence slice (so the <c>DbSet</c>-less
/// context's model contains the tenant and team entities), the <see cref="LocalAccountModelContributor"/> (so the model
/// contains the account entities), and a settable ambient tenant accessor.
/// </summary>
internal static class TestOpenIdDbContextSetup
{
    public static IServiceCollection AddTestOpenIdDbContext(
        this IServiceCollection serviceCollection,
        Action<DbContextOptionsBuilder> configureOptions
    )
    {
        serviceCollection.AddSingleton<ISurrogateKeyGenerator>(
            new SequentialSurrogateKeyGenerator()
        );
        serviceCollection.AddSingleton<IPageCursorProtector>(TestPageCursorProtector.Instance);
        serviceCollection.AddEntityFrameworkCorePersistence<OpenIdDbContext>();
        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Singleton<IOpenIdModelContributor, LocalAccountModelContributor>()
        );
        serviceCollection.AddSingleton<TestAmbientTenantAccessor>();
        serviceCollection.AddSingleton<IAmbientTenantAccessor>(serviceProvider =>
            serviceProvider.GetRequiredService<TestAmbientTenantAccessor>()
        );
        serviceCollection.AddDbContext<OpenIdDbContext>(configureOptions);
        return serviceCollection;
    }

    /// <summary>
    /// Finds the team with the given identifier, or seeds a minimal one directly into the context and saves it. Returns
    /// the team entity.
    /// </summary>
    public static TeamEntity SeedTeam(
        this OpenIdDbContext dbContext,
        ISurrogateKeyGenerator keyGenerator,
        string teamId
    )
    {
        var normalizedTeamId = teamId.ToLowerInvariant();
        var existing =
            dbContext
                .Set<TeamEntity>()
                .Local.FirstOrDefault(team => team.NormalizedTeamId == normalizedTeamId)
            ?? dbContext
                .Set<TeamEntity>()
                .FirstOrDefault(team => team.NormalizedTeamId == normalizedTeamId);
        if (existing is not null)
            return existing;

        var entity = new TeamEntity
        {
            Id = keyGenerator.CreateKey(),
            TeamId = teamId,
            NormalizedTeamId = normalizedTeamId,
            Path = $"/{teamId}/",
            ConcurrencyToken = string.Empty,
            SettingsConcurrencyToken = string.Empty,
            IsDisabled = false,
            DisplayName = "Platform",
            SettingsJson = JsonSerializer.SerializeToElement(new Dictionary<string, object>()),
            // The envelope the team store writes for a team that brings no root keys.
            RootKeysJson = JsonElement.Parse("""{"$ver":1,"$payload":{"rootKeys":{}}}"""),
            Tenants = new List<TenantEntity>(),
        };
        dbContext.Set<TeamEntity>().Add(entity);
        dbContext.SaveChanges();
        return entity;
    }

    /// <summary>
    /// Seeds a minimal persisted tenant directly into the context so tenant-scoped entities can reference it, and saves
    /// it. Returns the seeded tenant entity.
    /// </summary>
    public static TenantEntity SeedTenant(
        this OpenIdDbContext dbContext,
        ISurrogateKeyGenerator keyGenerator,
        string tenantId
    )
    {
        var team = dbContext.SeedTeam(keyGenerator, "platform");
        var entity = new TenantEntity
        {
            Id = keyGenerator.CreateKey(),
            TeamId = team.Id,
            Team = team,
            TenantId = tenantId,
            NormalizedTenantId = tenantId.ToLowerInvariant(),
            Issuer = null,
            NormalizedIssuer = null,
            NormalizedIssuerHost = null,
            MtlsEndpointAliasAuthority = null,
            NormalizedMtlsEndpointAliasHost = null,
            ConcurrencyToken = string.Empty,
            SettingsConcurrencyToken = string.Empty,
            SecretsConcurrencyToken = string.Empty,
            IsDisabled = false,
            DisplayName = tenantId,
            SettingsJson = JsonSerializer.SerializeToElement(new Dictionary<string, object>()),
            Secrets = [],
        };
        dbContext.Set<TenantEntity>().Add(entity);
        dbContext.SaveChanges();
        dbContext.SeedConnection(keyGenerator, entity, LocalConnectionId(tenantId), "local");
        return entity;
    }

    /// <summary>
    /// Gets the identifier of the <c>local</c> connection that <see cref="SeedTenant"/> seeds for the tenant.
    /// </summary>
    public static string LocalConnectionId(string tenantId) => $"{tenantId}-local";

    /// <summary>
    /// Seeds a minimal local connection of the tenant directly into the context and saves it. Returns the seeded
    /// connection entity.
    /// </summary>
    public static ConnectionEntity SeedConnection(
        this OpenIdDbContext dbContext,
        ISurrogateKeyGenerator keyGenerator,
        TenantEntity tenant,
        string connectionId,
        string name
    )
    {
        var entity = new ConnectionEntity
        {
            Id = keyGenerator.CreateKey(),
            TenantId = tenant.Id,
            Tenant = tenant,
            NormalizedTenantId = tenant.NormalizedTenantId,
            ConnectionId = connectionId,
            NormalizedConnectionId = connectionId.ToLowerInvariant(),
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            Strategy = "local",
            DisplayName = name,
            IsSystem = false,
            IsEnabled = true,
            OptionsJson = JsonElement.Parse("""{"store":"default"}"""),
            SettingsJson = JsonSerializer.SerializeToElement(new Dictionary<string, object>()),
            ConcurrencyToken = string.Empty,
            SettingsConcurrencyToken = string.Empty,
        };
        dbContext.Set<ConnectionEntity>().Add(entity);
        dbContext.SaveChanges();
        return entity;
    }
}

/// <summary>
/// An ambient tenant accessor whose tenant scope and unscoped scope a test begins and ends, so a test can leave data
/// access unset (matching no tenant row), scope it to a specific tenant, or open it across every tenant.
/// </summary>
internal sealed class TestAmbientTenantAccessor : IAmbientTenantAccessor
{
    public string? TenantId { get; set; }

    public bool IsScoped => TenantId is not null;

    public bool IsUnscoped { get; private set; }

    public string? UnscopedReason { get; private set; }

    public IDisposable BeginScope(string tenantId)
    {
        var previousTenantId = TenantId;
        TenantId = tenantId;
        return new Scope(() => TenantId = previousTenantId);
    }

    public IDisposable BeginUnscoped(string reason)
    {
        var previousIsUnscoped = IsUnscoped;
        var previousReason = UnscopedReason;
        IsUnscoped = true;
        UnscopedReason = reason;
        return new Scope(() =>
        {
            IsUnscoped = previousIsUnscoped;
            UnscopedReason = previousReason;
        });
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
