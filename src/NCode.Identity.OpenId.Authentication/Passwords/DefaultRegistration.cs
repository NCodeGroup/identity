// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Collections.Providers;
using NCode.Identity.Registration;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Authentication.Passwords;

/// <summary>
/// Provides extension methods to configure the password policy services.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder"/> to configure services for <see cref="OpenIdAuthenticationLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdAuthenticationLibrary> builder)
    {
        /// <summary>
        /// Configures the password policy, its built-in blocklists, the password attempt limiter, the password setting
        /// descriptors and the email-code setting descriptors. A deployment adds a blocklist by registering another
        /// <see cref="IPasswordBlocklist"/>.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdAuthenticationLibrary> AddPasswordServices()
        {
            var serviceCollection = builder.ServiceCollection;

            serviceCollection.TryAddSingleton(TimeProvider.System);
            serviceCollection.TryAddSingleton<IPasswordPolicy, DefaultPasswordPolicy>();
            serviceCollection.TryAddSingleton<
                IPasswordAttemptLimiter,
                DefaultPasswordAttemptLimiter
            >();
            var commonBlocklist = ServiceDescriptor.Singleton<
                IPasswordBlocklist,
                CommonPasswordBlocklist
            >();
            serviceCollection.TryAddEnumerable(commonBlocklist);
            var accountNameBlocklist = ServiceDescriptor.Singleton<
                IPasswordBlocklist,
                AccountNamePasswordBlocklist
            >();
            serviceCollection.TryAddEnumerable(accountNameBlocklist);
            var settingDataSource = ServiceDescriptor.Singleton<
                ICollectionDataSource<SettingDescriptor>,
                DefaultPasswordSettingDescriptorDataSource
            >();
            serviceCollection.TryAddEnumerable(settingDataSource);
            var emailCodeSettingDataSource = ServiceDescriptor.Singleton<
                ICollectionDataSource<SettingDescriptor>,
                DefaultEmailCodeSettingDescriptorDataSource
            >();
            serviceCollection.TryAddEnumerable(emailCodeSettingDataSource);

            return builder;
        }
    }
}
