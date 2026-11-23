// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Provides extension methods to configure the connection services.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder"/> to configure services for <see cref="OpenIdCoreLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdCoreLibrary> builder)
    {
        /// <summary>
        /// Configures the connection strategy catalog and the connection resolver (ADR-1535). A strategy is a
        /// registered <see cref="IConnectionStrategy"/>, one class per strategy.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdCoreLibrary> AddConnectionServices()
        {
            var serviceCollection = builder.ServiceCollection;

            serviceCollection.TryAddSingleton<
                IConnectionStrategyCatalog,
                DefaultConnectionStrategyCatalog
            >();
            serviceCollection.TryAddSingleton<IConnectionResolver, DefaultConnectionResolver>();

            return builder;
        }
    }
}
