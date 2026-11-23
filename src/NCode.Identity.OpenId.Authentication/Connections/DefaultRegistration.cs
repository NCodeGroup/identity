// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Authentication.Connections;

/// <summary>
/// Provides extension methods to configure the sign-in connection services.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder"/> to configure services for
    /// <see cref="OpenIdAuthenticationLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdAuthenticationLibrary> builder)
    {
        /// <summary>
        /// Configures the service that offers the connections of an interactive sign-in.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdAuthenticationLibrary> AddConnectionServices()
        {
            builder.ServiceCollection.TryAddSingleton<
                ISignInConnectionService,
                DefaultSignInConnectionService
            >();

            return builder;
        }
    }
}
