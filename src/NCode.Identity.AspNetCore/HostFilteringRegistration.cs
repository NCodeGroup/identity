// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore.Options;

namespace NCode.Identity.AspNetCore;

/// <summary>
/// Registers the validation that refuses a wildcard or empty <c>AllowedHosts</c> list.
/// </summary>
/// <remarks>
/// Safe to leave wired: a host configured for local development (<c>LocalDevelopment:Enabled</c>) may accept any host.
/// </remarks>
// See ADR-1370.
[PublicAPI]
public static class HostFilteringRegistration
{
    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the validation of <see cref="HostFilteringOptions"/> and runs it when the host starts, so a
        /// deployment whose <c>AllowedHosts</c> is empty or contains <c>*</c> stops at startup unless
        /// <c>LocalDevelopment:Enabled</c> is true. Calling it more than once registers the validation once.
        /// </summary>
        /// <param name="configuration">
        /// The <see cref="IConfiguration"/> read for the <c>LocalDevelopment</c> section.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for method chaining.</returns>
        public IServiceCollection AddHostFilteringValidation(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);
            ArgumentNullException.ThrowIfNull(configuration);

            serviceCollection.AddLocalDevelopmentOptions(configuration);

            // ValidateOnStart materializes the options as the host starts, so a wildcard list stops the host before
            // it accepts traffic (ADR-1137).
            serviceCollection.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IValidateOptions<HostFilteringOptions>,
                    HostFilteringOptionsValidator
                >()
            );
            serviceCollection.AddOptions<HostFilteringOptions>().ValidateOnStart();

            return serviceCollection;
        }
    }
}
