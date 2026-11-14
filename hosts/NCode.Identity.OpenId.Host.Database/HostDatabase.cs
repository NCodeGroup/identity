// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    ) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(MigrationsAssembly))
            .AddInterceptors(new TenantSessionInterceptor(ambientTenantAccessor));

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
    /// Registers the <see cref="RuntimeRolePostureCheck"/>, which refuses to start the host when the role it connects
    /// as is not subject to row-level security.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="connectionString">The Postgres connection string of the host's runtime role.</param>
    /// <param name="localDevelopment">
    /// Whether the host runs as a local inner-loop host (<c>LocalDevelopment:Enabled</c>), which warns instead of
    /// refusing to start.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRuntimeRolePostureCheck(
        this IServiceCollection services,
        string connectionString,
        bool localDevelopment
    ) =>
        services.AddHostedService(serviceProvider => new RuntimeRolePostureCheck(
            connectionString,
            localDevelopment,
            serviceProvider.GetRequiredService<ILogger<RuntimeRolePostureCheck>>()
        ));

    /// <summary>
    /// An unscoped <see cref="IAmbientTenantAccessor"/> used when a context is constructed outside a request (EF
    /// design-time tooling and the migration worker). Schema operations never read tenant-scoped data, so an unscoped
    /// accessor is sufficient and avoids resolving the request-scoped accessor.
    /// </summary>
    public sealed class UnscopedAmbientTenantAccessor : IAmbientTenantAccessor
    {
        /// <inheritdoc />
        public bool IsScoped => false;

        /// <inheritdoc />
        public string? TenantId => null;

        /// <inheritdoc />
        public bool IsUnscoped => true;

        /// <inheritdoc />
        public string? UnscopedReason => "schema migration";

        /// <inheritdoc />
        public IDisposable BeginScope(string tenantId) =>
            throw new NotSupportedException("Unscoped construction cannot begin a tenant scope.");

        /// <inheritdoc />
        public IDisposable BeginUnscoped(string reason) =>
            throw new NotSupportedException("Unscoped construction cannot begin another scope.");
    }
}
