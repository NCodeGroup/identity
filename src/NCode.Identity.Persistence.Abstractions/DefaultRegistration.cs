// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NCode.Identity.Persistence;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register the generator of the internal surrogate
/// keys of stored rows.
/// </summary>
[PublicAPI]
public static class DefaultRegistration
{
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to add services to.</param>
    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the default <see cref="ISurrogateKeyGenerator"/> as a singleton with <c>TryAdd</c>, so a generator
        /// registered before it is kept and a second call registers nothing.
        /// </summary>
        /// <returns>The same <see cref="IServiceCollection"/> instance for method chaining.</returns>
        [PublicAPI]
        public IServiceCollection AddSurrogateKeyGenerator()
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);

            serviceCollection.TryAddSingleton<
                ISurrogateKeyGenerator,
                DefaultSurrogateKeyGenerator
            >();

            return serviceCollection;
        }
    }
}
