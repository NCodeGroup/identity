// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Registers the startup check that refuses to start the host when the role it connects as is not subject to
/// row-level security with an <see cref="IServiceCollection"/>.
/// </summary>
[PublicAPI]
public static class RuntimeRolePostureCheckRegistration
{
    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the startup check that refuses to start the host when the role it connects as is not subject to
        /// row-level security.
        /// </summary>
        /// <param name="connectionString">The Postgres connection string of the host's runtime role.</param>
        /// <param name="localDevelopment">
        /// Whether the host runs as a local inner-loop host (<c>LocalDevelopment:Enabled</c>), which warns instead of
        /// refusing to start.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for method chaining.</returns>
        public IServiceCollection AddRuntimeRolePostureCheck(
            string connectionString,
            bool localDevelopment
        )
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);

            return serviceCollection.AddHostedService(
                serviceProvider => new RuntimeRolePostureCheck(
                    new NpgsqlRuntimeRolePostureReader(connectionString),
                    localDevelopment,
                    serviceProvider.GetRequiredService<ILogger<RuntimeRolePostureCheck>>()
                )
            );
        }
    }
}
