// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.EntityFramework;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Shared Npgsql wiring for the reference OP host database. The <c>api</c> host and the
/// <c>MigrationService</c> worker both configure the dev Postgres database through these helpers so the provider,
/// migrations assembly, and migrations-history tables stay a single source of truth.
/// </summary>
// See ADR-1073.
[PublicAPI]
public static class HostDatabase
{
    /// <summary>
    /// The assembly that owns the EF Core migrations for both the <see cref="OpenIdDbContext"/> and the
    /// <see cref="DeveloperDataProtectionDbContext"/>. Both contexts persist into one Postgres database, so the
    /// DataProtection context keeps its own migrations-history table to avoid colliding with the OpenID history.
    /// </summary>
    public static string MigrationsAssembly { get; } =
        typeof(HostDatabase).Assembly.GetName().Name
        ?? throw new InvalidOperationException("The host database assembly has no name.");

    private const string DataProtectionHistoryTable = "__EFMigrationsHistory_DataProtection";

    /// <summary>
    /// Configures the <see cref="OpenIdDbContext"/> options to use Npgsql against <paramref name="connectionString"/>,
    /// pointing migrations at this (the host database) assembly.
    /// </summary>
    /// <param name="options">The options builder to configure.</param>
    /// <param name="connectionString">The Postgres connection string (supplied by Aspire at run time).</param>
    /// <param name="ambientTenantAccessor">The accessor whose tenant scope the <see cref="TenantSessionInterceptor"/>
    /// carries into each Postgres session, so row-level security reads the scope the global query filter reads.</param>
    /// <returns>The same <paramref name="options"/> for chaining.</returns>
    public static DbContextOptionsBuilder UseOpenIdNpgsql(
        this DbContextOptionsBuilder options,
        string connectionString,
        IAmbientTenantAccessor ambientTenantAccessor
    )
    {
        var tenantSessionInterceptor = new TenantSessionInterceptor(ambientTenantAccessor);

        return options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssembly))
            .AddInterceptors(tenantSessionInterceptor);
    }

    /// <summary>
    /// Configures the <see cref="DeveloperDataProtectionDbContext"/> options to use Npgsql against
    /// <paramref name="connectionString"/>, with a dedicated migrations-history table so it can share the OpenID
    /// database without colliding with the OpenID migrations history.
    /// </summary>
    /// <param name="options">The options builder to configure.</param>
    /// <param name="connectionString">The Postgres connection string (supplied by Aspire at run time).</param>
    /// <returns>The same <paramref name="options"/> for chaining.</returns>
    public static DbContextOptionsBuilder UseDataProtectionNpgsql(
        this DbContextOptionsBuilder options,
        string connectionString
    ) =>
        options.UseNpgsql(
            connectionString,
            npgsql =>
                npgsql
                    .MigrationsAssembly(MigrationsAssembly)
                    .MigrationsHistoryTable(DataProtectionHistoryTable)
        );

    /// <summary>
    /// Creates an <see cref="OpenIdDbContext"/> outside a request, for the EF Core design-time tools and the migration
    /// runner. The context and its <see cref="TenantSessionInterceptor"/> share one
    /// <see cref="UnscopedAmbientTenantAccessor"/>, so its queries and its Postgres session reach every tenant's rows,
    /// and it takes its surrogate key generator and model contributors from <paramref name="serviceProvider"/>.
    /// </summary>
    /// <param name="serviceProvider">A provider that has the OpenID persistence slice registered (surrogate key
    /// generator and model contributors).</param>
    /// <param name="connectionString">The Postgres connection string.</param>
    /// <returns>The context, which the caller disposes.</returns>
    internal static OpenIdDbContext CreateUnscopedOpenIdContext(
        IServiceProvider serviceProvider,
        string connectionString
    )
    {
        var ambientTenantAccessor = new UnscopedAmbientTenantAccessor();

        var optionsBuilder = new DbContextOptionsBuilder<OpenIdDbContext>();
        optionsBuilder.UseOpenIdNpgsql(connectionString, ambientTenantAccessor);
        optionsBuilder.UseApplicationServiceProvider(serviceProvider);

        var modelContributors = serviceProvider.GetServices<IOpenIdModelContributor>();

        return new OpenIdDbContext(
            optionsBuilder.Options,
            ambientTenantAccessor,
            modelContributors,
            []
        );
    }

    /// <summary>
    /// Creates a <see cref="DeveloperDataProtectionDbContext"/> outside a request, for the EF Core design-time tools
    /// and the migration runner, configured by <see cref="UseDataProtectionNpgsql"/>.
    /// </summary>
    /// <param name="connectionString">The Postgres connection string.</param>
    /// <returns>The context, which the caller disposes.</returns>
    internal static DeveloperDataProtectionDbContext CreateDataProtectionContext(
        string connectionString
    )
    {
        var optionsBuilder = new DbContextOptionsBuilder<DeveloperDataProtectionDbContext>();
        optionsBuilder.UseDataProtectionNpgsql(connectionString);

        return new DeveloperDataProtectionDbContext(optionsBuilder.Options);
    }
}
