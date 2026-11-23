// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Environments;

/// <summary>
/// Provides extension methods to configure services and handlers for <see cref="OpenIdCoreLibrary"/>.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder"/> to configure services for <see cref="OpenIdCoreLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdCoreLibrary> builder)
    {
        /// <summary>
        /// Configures services and handlers for <see cref="OpenIdCoreLibrary"/>.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdCoreLibrary> AddEnvironmentServices()
        {
            var serviceCollection = builder.ServiceCollection;

            // The environment's protector protects under the host's Data Protection key ring, which a server persists
            // (ADR-1247).
            serviceCollection.AddDataProtection();

            serviceCollection.TryAddSingleton<
                IOpenIdEnvironmentFactory,
                DefaultOpenIdEnvironmentFactory
            >();
            serviceCollection.TryAddSingleton<
                IOpenIdEnvironmentProvider,
                DefaultOpenIdEnvironmentProvider
            >();

            return builder;
        }
    }
}
