// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Client.Options;
using NCode.Identity.Registration;
using NCode.Identity.Secrets.Collections;

namespace NCode.Identity.OpenId.Client.KeyRings;

/// <summary>
/// Provides extension methods for <see cref="IServiceBuilder{T}"/> to load the named key rings of a protocol client.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <summary>
    /// Gets whether a service descriptor registers the keyed <see cref="ISecretKeyCollectionProvider"/> of a key ring.
    /// </summary>
    /// <param name="descriptor">The service descriptor to check.</param>
    /// <param name="name">The name of the key ring.</param>
    /// <returns><see langword="true"/> when the descriptor registers the key ring's provider.</returns>
    private static bool IsKeyRingProvider(ServiceDescriptor descriptor, string name) =>
        descriptor.ServiceType == typeof(ISecretKeyCollectionProvider)
        && descriptor.IsKeyedService
        && Equals(descriptor.ServiceKey, name);

    /// <param name="builder">The <see cref="IServiceBuilder{T}"/> to configure services for
    /// <see cref="OpenIdClientLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdClientLibrary> builder)
    {
        /// <summary>
        /// Binds the named key rings under the top-level <c>KeyRings</c> section of <paramref name="configuration"/>
        /// strictly, so a key that maps to no option fails when the options are first read, and validates them when the
        /// host starts. Every key ring is loaded when the host starts, or when an offline command of the host runs its
        /// <see cref="IStartupPrerequisite"/> services, from PEM files and keys that a key management service holds,
        /// and a keyed <see cref="ISecretKeyCollectionProvider"/>, named after the key ring, returns the loaded key
        /// ring for every key ring configured when this method is called. A keyed provider resolves once the host
        /// starts: resolving it earlier, such as from an eager singleton or an options validator, throws an
        /// <see cref="InvalidOperationException"/> that names the key ring. A configured key ring that a keyed
        /// <see cref="ISecretKeyCollectionProvider"/> already provides when this method is called fails validation when
        /// the host starts, and the provider registered in code stays the one resolved.
        /// </summary>
        /// <param name="configuration">The configuration whose top-level <c>KeyRings</c> section has the key rings, by
        /// name, as its children.</param>
        /// <returns>The <see cref="IServiceBuilder{T}"/> so that additional calls can be chained.</returns>
        // See ADR-1439.
        // See ADR-1479.
        public IServiceBuilder<OpenIdClientLibrary> AddOpenIdClientKeyRings(
            IConfiguration configuration
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configuration);

            var section = configuration.GetSection(OpenIdClientDefaults.KeyRingsSectionName);
            var serviceCollection = builder.ServiceCollection;
            var names = section.GetChildren().Select(child => child.Key).ToList();

            var registeredInCode = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (serviceCollection.Any(descriptor => IsKeyRingProvider(descriptor, name)))
                    registeredInCode.Add(name);
            }

            serviceCollection
                .AddOptions<ClientKeyRingsOptions>()
                .Configure(options =>
                {
                    options.SectionPath = section.Path;
                    section.BindStrictly(options.KeyRings);
                    options.RegisteredInCode.UnionWith(registeredInCode);
                })
                .ValidateOnStart();
            var optionsValidator = ServiceDescriptor.Singleton<
                IValidateOptions<ClientKeyRingsOptions>,
                ClientKeyRingsOptionsValidator
            >();
            serviceCollection.TryAddEnumerable(optionsValidator);

            serviceCollection.TryAddSingleton<ClientKeyRings>();
            serviceCollection.TryAddSingleton<ClientRemoteKeyReader>();
            // One loader serves the host's start and the offline commands, which run every startup prerequisite
            // before their work, so the key rings load once whichever asks first.
            if (
                serviceCollection.All(descriptor =>
                    descriptor.ServiceType != typeof(ClientKeyRingLoader)
                )
            )
            {
                serviceCollection.AddSingleton<ClientKeyRingLoader>();
                serviceCollection.AddSingleton<IHostedService>(serviceProvider =>
                    serviceProvider.GetRequiredService<ClientKeyRingLoader>()
                );
                serviceCollection.AddSingleton<IStartupPrerequisite>(serviceProvider =>
                    serviceProvider.GetRequiredService<ClientKeyRingLoader>()
                );
            }

            foreach (var name in names)
            {
                if (registeredInCode.Contains(name))
                    continue;

                serviceCollection.AddKeyedSingleton<ISecretKeyCollectionProvider>(
                    name,
                    (serviceProvider, _) =>
                        serviceProvider.GetRequiredService<ClientKeyRings>().Get(name)
                );
            }

            return builder;
        }
    }
}
