// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.Authentication.Tokens.Commands;
using NCode.Identity.OpenId.Authentication.Tokens.Handlers;
using NCode.Identity.OpenId.Authentication.Tokens.ProofOfPossession;
using NCode.Identity.Registration;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Authentication.Tokens;

/// <summary>
/// Provides extension methods to configure services and handlers for OpenId Token services.
/// </summary>
internal static class DefaultRegistration
{
    /// <param name="builder">The <see cref="IServiceBuilder"/> to configure services for <see cref="OpenIdAuthenticationLibrary"/>.</param>
    extension(IServiceBuilder<OpenIdAuthenticationLibrary> builder)
    {
        /// <summary>
        /// Configures services and handlers for OpenId Token services.
        /// </summary>
        /// <returns>The <see cref="IServiceBuilder{T}"/> instance for method chaining.</returns>
        public IServiceBuilder<OpenIdAuthenticationLibrary> AddTokenServices()
        {
            var serviceCollection = builder.ServiceCollection;

            serviceCollection.TryAddSingleton<ITokenService, DefaultTokenService>();
            serviceCollection.TryAddSingleton<
                IAccessTokenPolicyService,
                DefaultAccessTokenPolicyService
            >();
            serviceCollection.TryAddSingleton<IIssuedTokenValidator, DefaultIssuedTokenValidator>();
            serviceCollection.TryAddSingleton<
                IAuthorizationCodeService,
                DefaultAuthorizationCodeService
            >();

            // Sender-constrained tokens (DPoP — RFC 9449, mutual-TLS — RFC 8705). The nonce and replay stores record
            // into the shared IExpiringKeySet, whose adapter the composition root registers (in-process for a single
            // replica, Valkey for several).
            serviceCollection.TryAddSingleton<
                IProofOfPossessionService,
                DefaultProofOfPossessionService
            >();
            serviceCollection.TryAddSingleton<IDPoPProofValidator, DefaultDPoPProofValidator>();
            serviceCollection.TryAddSingleton<IDPoPProofReplayCache, DefaultDPoPProofReplayCache>();
            serviceCollection.TryAddSingleton<IDPoPNonceService, DefaultDPoPNonceService>();

            serviceCollection.TryAddSingleton<
                ICommandResponseHandler<
                    AuthenticateAccessTokenCommand,
                    AuthenticateAccessTokenDisposition
                >,
                DefaultAuthenticateAccessTokenHandler
            >();

            var idTokenSubjectClaimsHandler = ServiceDescriptor.Singleton<
                ICommandHandler<GetIdTokenSubjectClaimsCommand>,
                DefaultGetIdTokenSubjectClaimsHandler
            >();
            serviceCollection.TryAddEnumerable(idTokenSubjectClaimsHandler);

            var idTokenPayloadClaimsHandler = ServiceDescriptor.Singleton<
                ICommandHandler<GetIdTokenPayloadClaimsCommand>,
                DefaultGetIdTokenPayloadClaimsHandler
            >();
            serviceCollection.TryAddEnumerable(idTokenPayloadClaimsHandler);

            var accessTokenSubjectClaimsHandler = ServiceDescriptor.Singleton<
                ICommandHandler<GetAccessTokenSubjectClaimsCommand>,
                DefaultGetAccessTokenSubjectClaimsHandler
            >();
            serviceCollection.TryAddEnumerable(accessTokenSubjectClaimsHandler);

            var accessTokenPayloadClaimsHandler = ServiceDescriptor.Singleton<
                ICommandHandler<GetAccessTokenPayloadClaimsCommand>,
                DefaultGetAccessTokenPayloadClaimsHandler
            >();
            serviceCollection.TryAddEnumerable(accessTokenPayloadClaimsHandler);

            var idTokenMetadataClaimsHandler = ServiceDescriptor.Singleton<
                ICommandHandler<GetIdTokenSubjectClaimsCommand>,
                DefaultGetIdTokenMetadataClaimsHandler
            >();
            serviceCollection.TryAddEnumerable(idTokenMetadataClaimsHandler);

            var accessTokenMetadataClaimsHandler = ServiceDescriptor.Singleton<
                ICommandHandler<GetAccessTokenSubjectClaimsCommand>,
                DefaultGetAccessTokenMetadataClaimsHandler
            >();
            serviceCollection.TryAddEnumerable(accessTokenMetadataClaimsHandler);

            return builder;
        }
    }
}
