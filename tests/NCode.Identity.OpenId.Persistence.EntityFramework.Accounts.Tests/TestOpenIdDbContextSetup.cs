// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using IdGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Accounts;

/// <summary>
/// Shared test setup for the <see cref="OpenIdDbContext"/> over the local-account slice: registers the id generator,
/// the reusable framework persistence services, the <see cref="LocalAccountModelContributor"/> (so the <c>DbSet</c>-less
/// context's model contains the account entities), and a settable ambient tenant accessor.
/// </summary>
internal static class TestOpenIdDbContextSetup
{
    public static IServiceCollection AddTestOpenIdDbContext(
        this IServiceCollection serviceCollection,
        Action<DbContextOptionsBuilder> configureOptions
    )
    {
        serviceCollection.AddSingleton<IIdGenerator<long>>(new IdGenerator(0));
        serviceCollection.AddEntityFrameworkPersistenceServices<OpenIdDbContext>();
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
