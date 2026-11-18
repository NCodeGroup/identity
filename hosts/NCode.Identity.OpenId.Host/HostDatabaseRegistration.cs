// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Host.Database;
using NCode.Identity.OpenId.Persistence.EntityFramework;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Host;

/// <summary>
/// Registers the reference host's database with an <see cref="IServiceCollection"/>.
/// </summary>
// See ADR-1073.
internal static class HostDatabaseRegistration
{
    /// <summary>
    /// The name of the host database's readiness check, <c>openiddb</c>.
    /// </summary>
    public const string HealthCheckName = "openiddb";

    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the host's database from the connection that <see cref="HostDatabaseConnection"/> resolves from
        /// <paramref name="configuration"/>. With a connection, the <see cref="OpenIdDbContext"/> factory and the
        /// <see cref="DeveloperDataProtectionDbContext"/> use Npgsql, each OpenID context carries the ambient tenant
        /// scope into its Postgres session, the Data Protection key ring is kept in the database, a startup check
        /// refuses a runtime role that is not subject to row-level security (it only warns while
        /// <c>LocalDevelopment:Enabled</c> is set), and the <see cref="HealthCheckName"/> readiness check holds the
        /// replica out of rotation while it cannot reach the database. Without a connection, the OpenID context factory
        /// uses an in-memory database and nothing else is registered.
        /// </summary>
        /// <param name="configuration">
        /// The configuration that holds the connection and the <c>LocalDevelopment</c> section.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">
        /// The connection's configuration or the <c>LocalDevelopment</c> section is invalid.
        /// </exception>
        public IServiceCollection AddHostDatabase(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);
            ArgumentNullException.ThrowIfNull(configuration);

            var connectionString = HostDatabaseConnection.Resolve(configuration);
            if (string.IsNullOrEmpty(connectionString))
            {
                // Hermetic test hosts boot the composition root without a connection and replace the OpenID context and
                // the Data Protection key ring with stores of their own; Data Protection otherwise keeps its default
                // repository, which AddDeploymentSafety refuses.
                serviceCollection.AddDbContextFactory<OpenIdDbContext>(dbOptions =>
                    dbOptions.UseInMemoryDatabase("OpenId")
                );
                return serviceCollection;
            }

            serviceCollection.AddDbContextFactory<OpenIdDbContext>(
                (serviceProvider, dbOptions) =>
                {
                    var ambientTenantAccessor =
                        serviceProvider.GetRequiredService<IAmbientTenantAccessor>();
                    dbOptions.UseOpenIdNpgsql(connectionString, ambientTenantAccessor);
                }
            );
            serviceCollection.AddDbContext<DeveloperDataProtectionDbContext>(dbOptions =>
                dbOptions.UseDataProtectionNpgsql(connectionString)
            );
            serviceCollection
                .AddDataProtection()
                .PersistKeysToDbContext<DeveloperDataProtectionDbContext>()
                .SetApplicationName("NCode.Identity.OpenId.Host");

            // The host refuses to start when the role it connects as is a superuser, bypasses row-level security or
            // owns the tables, because the tenant policies would not apply to it; 'LocalDevelopment:Enabled' only warns
            // (ADR-1370).
            var localDevelopment = configuration.IsLocalDevelopment();
            serviceCollection.AddRuntimeRolePostureCheck(connectionString, localDevelopment);

            // The replica is ready only while it can reach the database it serves every request from.
            serviceCollection
                .AddHealthChecks()
                .AddDbContextCheck<OpenIdDbContext>(
                    HealthCheckName,
                    tags: [HealthCheckTags.Readiness]
                );

            return serviceCollection;
        }
    }
}
