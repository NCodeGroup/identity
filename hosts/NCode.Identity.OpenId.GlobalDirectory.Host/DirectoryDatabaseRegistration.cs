// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory.Server;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.GlobalDirectory.Host;

/// <summary>
/// Registers the directory's own database with an <see cref="IServiceCollection"/>.
/// </summary>
internal static class DirectoryDatabaseRegistration
{
    /// <summary>The name of the directory database's readiness check, <c>directory</c>.</summary>
    public const string HealthCheckName = "directory";

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
                    HealthCheckName,
                    tags: [HealthCheckTags.Readiness]
                );

            return serviceCollection;
        }
    }
}
