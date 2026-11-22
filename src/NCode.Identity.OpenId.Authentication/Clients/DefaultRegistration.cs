// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Collections.Providers;
using NCode.Identity.OpenId.Authentication.Clients.Coherence;
using NCode.Identity.OpenId.Authentication.Clients.Handlers;
using NCode.Identity.OpenId.Teams;
using NCode.Identity.Registration;
using NCode.Identity.Settings;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Authentication.Clients;

/// <summary>
/// Provides extension methods to configure services and handlers for OpenId Client services.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder"/> to configure services for <see cref="OpenIdAuthenticationLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdAuthenticationLibrary> builder)
    {
        /// <summary>
        /// Configures services and handlers for OpenId Client services.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdAuthenticationLibrary> AddClientServices()
        {
            var serviceCollection = builder.ServiceCollection;

            serviceCollection.TryAddSingleton<IOpenIdClientFactory, DefaultOpenIdClientFactory>();

            serviceCollection.TryAddSingleton<IOpenIdClientResolver, DefaultOpenIdClientResolver>();

            serviceCollection.TryAddSingleton<IClientKeyImporter, DefaultClientKeyImporter>();

            serviceCollection.TryAddSingleton<IClientSecretIssuer, DefaultClientSecretIssuer>();

            serviceCollection.TryAddSingleton<IClientKeyResolver, DefaultClientKeyResolver>();

            serviceCollection.TryAddSingleton<
                IClientJwtCredentialSelector,
                DefaultClientJwtCredentialSelector
            >();

            serviceCollection.TryAddSingleton<
                IClientAuthenticationService,
                DefaultClientAuthenticationService
            >();

            serviceCollection.TryAddSingleton<
                IClientAssertionReplayCache,
                DefaultClientAssertionReplayCache
            >();

            // Failed client authentications are limited per remote address before any credential is verified, in the
            // shared counter set the composition root registers (ADR-1399).
            serviceCollection.TryAddSingleton(TimeProvider.System);
            serviceCollection.TryAddSingleton<
                IClientAuthenticationAttemptLimiter,
                DefaultClientAuthenticationAttemptLimiter
            >();
            var defaultClientAuthenticationAttemptSettingDescriptorDataSource =
                ServiceDescriptor.Singleton<
                    ICollectionDataSource<SettingDescriptor>,
                    DefaultClientAuthenticationAttemptSettingDescriptorDataSource
                >();
            serviceCollection.TryAddEnumerable(
                defaultClientAuthenticationAttemptSettingDescriptorDataSource
            );

            // Each client-authentication handler below has a descriptor of the same name, so the methods are named
            // without constructing a handler (ADR-1145).
            var defaultClientAuthenticationMethodDataSource = ServiceDescriptor.Singleton<
                ICollectionDataSource<ClientAuthenticationMethodDescriptor>,
                DefaultClientAuthenticationMethodDataSource
            >();
            serviceCollection.TryAddEnumerable(defaultClientAuthenticationMethodDataSource);

            serviceCollection.TryAddSingleton<
                IClientAuthenticationMethodCatalog,
                DefaultClientAuthenticationMethodCatalog
            >();

            var clientSecretBasicClientAuthenticationHandler = ServiceDescriptor.Singleton<
                IClientAuthenticationHandler,
                ClientSecretBasicClientAuthenticationHandler
            >();
            serviceCollection.TryAddEnumerable(clientSecretBasicClientAuthenticationHandler);

            var clientSecretPostClientAuthenticationHandler = ServiceDescriptor.Singleton<
                IClientAuthenticationHandler,
                ClientSecretPostClientAuthenticationHandler
            >();
            serviceCollection.TryAddEnumerable(clientSecretPostClientAuthenticationHandler);

            var privateKeyJwtClientAuthenticationHandler = ServiceDescriptor.Singleton<
                IClientAuthenticationHandler,
                PrivateKeyJwtClientAuthenticationHandler
            >();
            serviceCollection.TryAddEnumerable(privateKeyJwtClientAuthenticationHandler);

            var clientSecretJwtClientAuthenticationHandler = ServiceDescriptor.Singleton<
                IClientAuthenticationHandler,
                ClientSecretJwtClientAuthenticationHandler
            >();
            serviceCollection.TryAddEnumerable(clientSecretJwtClientAuthenticationHandler);

            var tlsClientAuthClientAuthenticationHandler = ServiceDescriptor.Singleton<
                IClientAuthenticationHandler,
                TlsClientAuthClientAuthenticationHandler
            >();
            serviceCollection.TryAddEnumerable(tlsClientAuthClientAuthenticationHandler);

            var selfSignedTlsClientAuthClientAuthenticationHandler = ServiceDescriptor.Singleton<
                IClientAuthenticationHandler,
                SelfSignedTlsClientAuthClientAuthenticationHandler
            >();
            serviceCollection.TryAddEnumerable(selfSignedTlsClientAuthClientAuthenticationHandler);

            var noneClientAuthenticationHandler = ServiceDescriptor.Singleton<
                IClientAuthenticationHandler,
                NoneClientAuthenticationHandler
            >();
            serviceCollection.TryAddEnumerable(noneClientAuthenticationHandler);

            serviceCollection.TryAddSingleton<
                IClientCoherenceValidator,
                DefaultClientCoherenceValidator
            >();

            serviceCollection.TryAddSingleton<
                IClientCoherenceReporter,
                DefaultClientCoherenceReporter
            >();

            // The validator runs every registered rule, so a package adds a coherence rule the same way: one
            // TryAddEnumerable registration of its IClientCoherenceRule (ADR-1206).
            var responseAlgorithmCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                ResponseAlgorithmCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(responseAlgorithmCoherenceRule);

            var redirectUriKindCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                RedirectUriKindCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(redirectUriKindCoherenceRule);

            var tokenEndpointAuthMethodCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                TokenEndpointAuthMethodCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(tokenEndpointAuthMethodCoherenceRule);

            var certificateBoundAccessTokenCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                CertificateBoundAccessTokenCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(certificateBoundAccessTokenCoherenceRule);

            var dPoPBoundAccessTokenCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                DPoPBoundAccessTokenCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(dPoPBoundAccessTokenCoherenceRule);

            var registeredValueCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                RegisteredValueCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(registeredValueCoherenceRule);

            var clientSecretKeyCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                ClientSecretKeyCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(clientSecretKeyCoherenceRule);

            var idTokenEncryptionCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                IdTokenEncryptionCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(idTokenEncryptionCoherenceRule);

            var logoutChannelCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                LogoutChannelCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(logoutChannelCoherenceRule);

            var backchannelTokenDeliveryCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                BackchannelTokenDeliveryCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(backchannelTokenDeliveryCoherenceRule);

            var refreshTokenRotationRetryCoherenceRule = ServiceDescriptor.Singleton<
                IClientCoherenceRule,
                RefreshTokenRotationRetryCoherenceRule
            >();
            serviceCollection.TryAddEnumerable(refreshTokenRotationRetryCoherenceRule);

            // A change of a team's effective settings that is not a write is reported client by client (ADR-1412).
            var clientCoherenceReportHandler = ServiceDescriptor.Singleton<
                ICommandHandler<ReconcileTeamSettingsCommand>,
                ClientCoherenceReportHandler
            >();
            serviceCollection.TryAddEnumerable(clientCoherenceReportHandler);

            return builder;
        }
    }
}
