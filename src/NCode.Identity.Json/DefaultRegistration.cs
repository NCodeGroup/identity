// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NCode.Identity.Json;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register the schema-versioned serializer of the
/// payloads stored at rest as JSON.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add services to.</param>
    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the default <see cref="IPersistedPayloadSerializer"/> as a singleton with <c>TryAdd</c>, so a
        /// serializer registered before it is kept and a second call registers nothing. The serializer chains the
        /// <see cref="IPersistedPayloadUpcaster"/> steps registered as an enumerable.
        /// </summary>
        /// <returns>The same <see cref="IServiceCollection"/> instance for method chaining.</returns>
        [PublicAPI]
        public IServiceCollection AddPersistedPayloadSerializer()
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);

            serviceCollection.TryAddSingleton<
                IPersistedPayloadSerializer,
                DefaultPersistedPayloadSerializer
            >();

            return serviceCollection;
        }
    }
}
