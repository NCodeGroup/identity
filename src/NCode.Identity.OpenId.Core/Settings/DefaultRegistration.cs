// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NCode.Collections.Providers;
using NCode.Identity.OpenId.Locales;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.OpenId.Tenants.Keys;
using NCode.Identity.Registration;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Settings;

/// <summary>
/// Provides extension methods to configure the base OpenID setting-descriptor catalog and default settings.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder"/> to configure services for <see cref="OpenIdCoreLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdCoreLibrary> builder)
    {
        /// <summary>
        /// Configures the base setting-descriptor catalog, the built-in default settings provider, and the validation of
        /// the platform's configured settings. The catalog is a composable collection; the authentication slice contributes
        /// its protocol-specific descriptors additively. Setting names are a closed vocabulary, so a configured setting
        /// with no registered descriptor, or a value that does not convert to its type, stops the host when it starts.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdCoreLibrary> AddSettingServices()
        {
            var serviceCollection = builder.ServiceCollection;

            var defaultSettingDescriptorDataSource = ServiceDescriptor.Singleton<
                ICollectionDataSource<SettingDescriptor>,
                DefaultSettingDescriptorDataSource
            >();
            serviceCollection.TryAddEnumerable(defaultSettingDescriptorDataSource);

            // The scopes of the settings cascade are registered data, so an extension adds a scope for an entity of its
            // own beneath the scope it inherits from (ADR-1149, ADR-1150).
            var defaultSettingScopeDataSource = ServiceDescriptor.Singleton<
                ICollectionDataSource<SettingScopeDescriptor>,
                DefaultSettingScopeDataSource
            >();
            serviceCollection.TryAddEnumerable(defaultSettingScopeDataSource);

            var defaultSettingsProvider = ServiceDescriptor.Singleton<
                IDefaultSettingsProvider,
                DefaultSettingsProvider
            >();
            serviceCollection.TryAddEnumerable(defaultSettingsProvider);

            // The platform's configured settings are a closed vocabulary typed by the descriptor catalog; a configured
            // setting that cannot be honored stops the host when it starts (ADR-1137, ADR-1139).
            serviceCollection.TryAddSingleton<
                ISettingConfigurationReader,
                DefaultSettingConfigurationReader
            >();
            serviceCollection.AddOptions<OpenIdOptions>().ValidateOnStart();
            var platformSettingsOptionsValidator = ServiceDescriptor.Singleton<
                IValidateOptions<OpenIdOptions>,
                PlatformSettingsOptionsValidator
            >();
            serviceCollection.TryAddEnumerable(platformSettingsOptionsValidator);

            // The security-profile catalog resolves the registered profiles (a strategy collection contributed by the
            // slices that define them) into their ceiling settings and request-time requirements.
            serviceCollection.TryAddSingleton<
                ISecurityProfileCatalog,
                DefaultSecurityProfileCatalog
            >();
            var productionSecurityProfile = ServiceDescriptor.Singleton<
                ISecurityProfile,
                ProductionSecurityProfile
            >();
            serviceCollection.TryAddEnumerable(productionSecurityProfile);

            // Each built-in type is its own class: TryAddEnumerable keeps one registration per implementation type
            // (ADR-1289).
            var developmentEnvironmentType = ServiceDescriptor.Singleton<
                ITenantEnvironmentType,
                DevelopmentTenantEnvironmentType
            >();
            serviceCollection.TryAddEnumerable(developmentEnvironmentType);
            var testingEnvironmentType = ServiceDescriptor.Singleton<
                ITenantEnvironmentType,
                TestingTenantEnvironmentType
            >();
            serviceCollection.TryAddEnumerable(testingEnvironmentType);
            var stagingEnvironmentType = ServiceDescriptor.Singleton<
                ITenantEnvironmentType,
                StagingTenantEnvironmentType
            >();
            serviceCollection.TryAddEnumerable(stagingEnvironmentType);
            var productionEnvironmentType = ServiceDescriptor.Singleton<
                ITenantEnvironmentType,
                ProductionTenantEnvironmentType
            >();
            serviceCollection.TryAddEnumerable(productionEnvironmentType);

            // The UI locale catalog resolves the locales the end-user interface registers, which is the domain of the
            // ui_locales_supported and default_ui_locale settings; it reads only data, so the descriptors consult it
            // inside options validation (ADR-1402).
            serviceCollection.TryAddSingleton<IUiLocaleCatalog, DefaultUiLocaleCatalog>();

            // The tenant environment catalog resolves the registered types by name, and the settings that govern them are
            // contributed beside it as descriptors.
            serviceCollection.TryAddSingleton<
                ITenantEnvironmentCatalog,
                DefaultTenantEnvironmentCatalog
            >();
            var tenantEnvironmentSettingDescriptorDataSource = ServiceDescriptor.Singleton<
                ICollectionDataSource<SettingDescriptor>,
                TenantEnvironmentSettingDescriptorDataSource
            >();
            serviceCollection.TryAddEnumerable(tenantEnvironmentSettingDescriptorDataSource);

            // The setting that names the remote key provider holding a tenant's keys is contributed beside it; its
            // domain is the providers the remote key provider catalog registers.
            var tenantKeyProviderSettingDescriptorDataSource = ServiceDescriptor.Singleton<
                ICollectionDataSource<SettingDescriptor>,
                TenantKeyProviderSettingDescriptorDataSource
            >();
            serviceCollection.TryAddEnumerable(tenantKeyProviderSettingDescriptorDataSource);

            // The effective-settings resolver is the one place that resolves a scope's effective settings outside the
            // live providers; a host can replace it (ADR-1204).
            serviceCollection.TryAddSingleton<
                IEffectiveSettingsResolver,
                DefaultEffectiveSettingsResolver
            >();

            // Each built-in kind is its own class: TryAddEnumerable keeps one registration per implementation type
            // (ADR-1203).
            var httpsRedirectUriKind = ServiceDescriptor.Singleton<
                IRedirectUriKind,
                HttpsRedirectUriKind
            >();
            serviceCollection.TryAddEnumerable(httpsRedirectUriKind);
            var loopbackRedirectUriKind = ServiceDescriptor.Singleton<
                IRedirectUriKind,
                LoopbackRedirectUriKind
            >();
            serviceCollection.TryAddEnumerable(loopbackRedirectUriKind);
            var httpRedirectUriKind = ServiceDescriptor.Singleton<
                IRedirectUriKind,
                HttpRedirectUriKind
            >();
            serviceCollection.TryAddEnumerable(httpRedirectUriKind);
            var privateUseRedirectUriKind = ServiceDescriptor.Singleton<
                IRedirectUriKind,
                PrivateUseRedirectUriKind
            >();
            serviceCollection.TryAddEnumerable(privateUseRedirectUriKind);

            // The redirect URI kind catalog resolves the registered kinds by name and judges a redirect URI against the
            // redirect_uri_kinds_supported setting.
            serviceCollection.TryAddSingleton<
                IRedirectUriKindCatalog,
                DefaultRedirectUriKindCatalog
            >();

            return builder;
        }
    }
}
