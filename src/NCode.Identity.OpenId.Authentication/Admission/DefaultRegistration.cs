// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NCode.Collections.Providers;
using NCode.Identity.OpenId.Authentication.Metrics;
using NCode.Identity.Registration;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Authentication.Admission;

/// <summary>
/// Provides extension methods to configure the per-replica admission services.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder"/> to configure services for
    /// <see cref="OpenIdAuthenticationLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdAuthenticationLibrary> builder)
    {
        /// <summary>
        /// Configures the per-replica admission gate that sheds a tenant's protocol requests once they fill its share
        /// of the replica's capacity, its options and its setting descriptor; the OpenID endpoint group installs the
        /// gate's endpoint filter.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdAuthenticationLibrary> AddAdmissionServices()
        {
            var serviceCollection = builder.ServiceCollection;

            // See ADR-1523.
            serviceCollection.AddMetrics();
            serviceCollection.TryAddSingleton<AuthenticationMetrics>();
            serviceCollection.TryAddSingleton<IRequestAdmissionGate, DefaultRequestAdmissionGate>();
            var optionsValidator = ServiceDescriptor.Singleton<
                IValidateOptions<AdmissionOptions>,
                AdmissionOptionsValidator
            >();
            serviceCollection.TryAddEnumerable(optionsValidator);
            serviceCollection.AddOptions<AdmissionOptions>().ValidateOnStart();
            var settingDataSource = ServiceDescriptor.Singleton<
                ICollectionDataSource<SettingDescriptor>,
                DefaultAdmissionSettingDescriptorDataSource
            >();
            serviceCollection.TryAddEnumerable(settingDataSource);

            return builder;
        }
    }
}
