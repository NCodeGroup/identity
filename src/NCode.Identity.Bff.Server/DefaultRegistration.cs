// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NCode.Identity.Bff.Server;

/// <summary>
/// Provides extension methods to compose a backend for frontend (BFF).
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to configure services.</param>
    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Composes a backend for frontend in one call: the shared-state (in-process by default), events, secrets,
        /// JOSE, JWT and OpenID protocol client libraries, and the BFF library configured from the <c>Bff</c> section
        /// of <paramref name="configuration"/>. The host's <c>AllowedHosts</c> is validated when the host starts: it
        /// must list the deployment's host names and not be empty or contain <c>*</c>, unless
        /// <c>LocalDevelopment:Enabled</c> is true. The host still provides what is unsafe to default: the key rings
        /// its configuration names, and a shared-state adapter when it runs several replicas.
        /// </summary>
        /// <param name="configuration">
        /// The configuration whose <c>Bff</c> section configures the BFF, whose <c>KeyRings</c> section configures its
        /// key rings and whose <c>LocalDevelopment</c> section turns on the relaxations a local inner-loop host needs.
        /// </param>
        /// <returns>The <see cref="IIdentityBffBuilder"/> instance for additional configuration.</returns>
        public IIdentityBffBuilder AddIdentityBff(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);
            ArgumentNullException.ThrowIfNull(configuration);

            return new IdentityBffBuilder(serviceCollection, configuration);
        }
    }
}
