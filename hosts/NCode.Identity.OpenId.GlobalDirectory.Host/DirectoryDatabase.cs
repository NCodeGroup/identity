// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.GlobalDirectory.Server;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.GlobalDirectory.Host;

/// <summary>
/// Npgsql wiring for the directory's own database. The host and the EF Core design-time factory configure the database
/// through these helpers, so the provider and the migrations assembly have one source of truth.
/// </summary>
internal static class DirectoryDatabase
{
    /// <summary>
    /// The assembly that owns the EF Core migrations for the <see cref="DirectoryDbContext"/>. The directory server
    /// package ships no migrations.
    /// </summary>
    private static string MigrationsAssembly { get; } =
        typeof(DirectoryDatabase).Assembly.GetName().Name
        ?? throw new InvalidOperationException("The directory host assembly has no name.");

    extension(DbContextOptionsBuilder builder)
    {
        /// <summary>
        /// Configures the options to use Npgsql against <paramref name="connectionString"/>, pointing migrations at this
        /// (the directory host) assembly.
        /// </summary>
        /// <param name="connectionString">The Postgres connection string.</param>
        /// <returns>The same <see cref="DbContextOptionsBuilder"/> instance for method chaining.</returns>
        public DbContextOptionsBuilder UseDirectoryNpgsql(string connectionString)
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(MigrationsAssembly)
            );
        }
    }

    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the <see cref="DirectoryDbContext"/> factory against <paramref name="connectionString"/> and the
        /// <c>directory</c> readiness check, which holds the replica out of rotation while it cannot reach the database
        /// it serves every request from.
        /// </summary>
        /// <param name="connectionString">The Postgres connection string.</param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for method chaining.</returns>
        public IServiceCollection AddDirectoryDatabase(string connectionString)
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);

            serviceCollection.AddDbContextFactory<DirectoryDbContext>(dbOptions =>
                dbOptions.UseDirectoryNpgsql(connectionString)
            );

            serviceCollection
                .AddHealthChecks()
                .AddDbContextCheck<DirectoryDbContext>(
                    "directory",
                    tags: [HealthCheckTags.Readiness]
                );

            return serviceCollection;
        }
    }
}
