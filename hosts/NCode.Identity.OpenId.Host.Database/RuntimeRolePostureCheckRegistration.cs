// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Host.Database.Facades;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Registers the startup check that refuses to start the host when the role it connects as is not subject to row-level
/// security with an <see cref="IServiceCollection"/>.
/// </summary>
[PublicAPI]
public static class RuntimeRolePostureCheckRegistration
{
    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the startup check that refuses to start the host when the role it connects as is not subject to
        /// row-level security. It registers a <see cref="LocalDevelopmentMode"/> that is off when none is registered,
        /// so the check refuses such a role; the host's <c>AddLocalDevelopmentOptions</c> call replaces it with the
        /// mode its configuration selects, which the check reads when the host starts and which only warns under
        /// <c>LocalDevelopment:Enabled</c>.
        /// </summary>
        /// <param name="connectionString">The Postgres connection string of the host's runtime role.</param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for method chaining.</returns>
        public IServiceCollection AddRuntimeRolePostureCheck(string connectionString)
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);

            var disabledMode = new LocalDevelopmentMode(false);
            serviceCollection.TryAddSingleton(disabledMode);

            return serviceCollection.AddHostedService(serviceProvider =>
            {
                var postureReader = new NpgsqlRuntimeRolePostureReader(connectionString);
                var localDevelopmentMode =
                    serviceProvider.GetRequiredService<LocalDevelopmentMode>();
                var logger = serviceProvider.GetRequiredService<ILogger<RuntimeRolePostureCheck>>();

                return new RuntimeRolePostureCheck(postureReader, localDevelopmentMode, logger);
            });
        }
    }
}
