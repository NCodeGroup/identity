// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.Registration;
using NCode.Identity.Secrets.Collections;

namespace NCode.Identity.OpenId.Client.KeyRings;

/// <summary>
/// Provides extension methods for <see cref="IServiceBuilder{T}"/> to load the named key rings of a protocol client.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder{T}"/> to configure services for
    /// <see cref="OpenIdClientLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdClientLibrary> builder)
    {
        /// <summary>
        /// Binds the named key rings under <paramref name="section"/> strictly, so a key that maps to no option fails
        /// when the options are first read, and validates them when the host starts. Every key ring is loaded when the
        /// host starts, from PEM files and keys that a key management service holds, and a keyed
        /// <see cref="ISecretKeyCollectionProvider"/>, named after the key ring, returns the loaded key ring for every
        /// key ring configured when this method is called. A keyed provider resolves once the host starts: resolving
        /// it earlier, such as from an eager singleton or an options validator, throws an
        /// <see cref="InvalidOperationException"/> that names the key ring.
        /// </summary>
        /// <param name="section">The configuration section whose children are the key rings, by name.</param>
        /// <returns>The <see cref="IServiceBuilder{T}"/> so that additional calls can be chained.</returns>
        /// <exception cref="InvalidOperationException">A key ring is already registered as a keyed
        /// <see cref="ISecretKeyCollectionProvider"/> under its name.</exception>
        // See ADR-1439.
        // See ADR-1479.
        public IServiceBuilder<OpenIdClientLibrary> AddOpenIdClientKeyRings(
            IConfigurationSection section
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(section);

            var serviceCollection = builder.ServiceCollection;
            var names = section.GetChildren().Select(child => child.Key).ToList();

            foreach (var name in names)
            {
                if (
                    serviceCollection.Any(descriptor =>
                        descriptor.ServiceType == typeof(ISecretKeyCollectionProvider)
                        && descriptor.IsKeyedService
                        && Equals(descriptor.ServiceKey, name)
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"The key ring '{name}' of '{section.Path}' is already registered as a keyed {nameof(ISecretKeyCollectionProvider)}."
                    );
                }
            }

            serviceCollection
                .AddOptions<ClientKeyRingsOptions>()
                .Configure(options =>
                {
                    options.SectionPath = section.Path;
                    section.BindStrictly(options.KeyRings);
                })
                .ValidateOnStart();
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IValidateOptions<ClientKeyRingsOptions>,
                    ClientKeyRingsOptionsValidator
                >()
            );

            serviceCollection.TryAddSingleton<ClientKeyRings>();
            serviceCollection.TryAddSingleton<ClientRemoteKeyReader>();
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, ClientKeyRingLoader>()
            );

            foreach (var name in names)
            {
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
