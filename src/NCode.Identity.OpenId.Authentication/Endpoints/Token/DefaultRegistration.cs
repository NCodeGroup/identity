// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.AuthorizationCode;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.Ciba;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.ClientCredentials;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.Commands;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.DeviceCode;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.Grants;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.Handlers;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.Messages;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.Password;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.RefreshToken;
using NCode.Identity.OpenId.Authentication.Endpoints.Token.TokenExchange;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Messages;
using NCode.Identity.Registration.AspNetCore;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Authentication.Endpoints.Token;

/// <summary>
/// Provides extension methods to configure services and handlers for the OpenId Token endpoint.
/// </summary>
internal static class DefaultRegistration
{
    extension(IEndpointGroupBuilder builder)
    {
        /// <summary>
        /// Configures services and handlers for the OpenId Token endpoint.
        /// </summary>
        public void AddTokenEndpoint()
        {
            builder.AddEndpoint<DefaultTokenEndpointProvider>();

            builder.AddMessageFactory<TokenRequest>();
            builder.AddMessageFactory<TokenResponse>();

            var serviceCollection = builder.ServiceCollection;

            // Handlers

            serviceCollection.TryAddSingleton<
                ICommandResponseHandler<SelectTokenGrantHandlerCommand, ITokenGrantHandler>,
                DefaultSelectTokenGrantHandlerHandler
            >();

            var validateTokenRequestHandler = ServiceDescriptor.Singleton<
                ICommandHandler<ValidateTokenRequestCommand>,
                DefaultValidateTokenRequestHandler
            >();
            serviceCollection.TryAddEnumerable(validateTokenRequestHandler);

            // Authorization Code

            var authorizationCodeGrantHandler = ServiceDescriptor.Singleton<
                ITokenGrantHandler,
                DefaultAuthorizationCodeGrantHandler
            >();
            serviceCollection.TryAddEnumerable(authorizationCodeGrantHandler);

            var validateAuthorizationCodeGrantHandler = ServiceDescriptor.Singleton<
                ICommandHandler<ValidateTokenGrantCommand<AuthorizationGrant>>,
                DefaultValidateAuthorizationCodeGrantHandler
            >();
            serviceCollection.TryAddEnumerable(validateAuthorizationCodeGrantHandler);

            // Refresh Token

            var refreshTokenGrantHandler = ServiceDescriptor.Singleton<
                ITokenGrantHandler,
                DefaultRefreshTokenGrantHandler
            >();
            serviceCollection.TryAddEnumerable(refreshTokenGrantHandler);

            var validateRefreshTokenGrantHandler = ServiceDescriptor.Singleton<
                ICommandHandler<ValidateTokenGrantCommand<RefreshTokenGrant>>,
                DefaultValidateRefreshTokenGrantHandler
            >();
            serviceCollection.TryAddEnumerable(validateRefreshTokenGrantHandler);

            // Device Code

            var deviceCodeGrantHandler = ServiceDescriptor.Singleton<
                ITokenGrantHandler,
                DefaultDeviceCodeGrantHandler
            >();
            serviceCollection.TryAddEnumerable(deviceCodeGrantHandler);

            var validateDeviceCodeGrantHandler = ServiceDescriptor.Singleton<
                ICommandHandler<ValidateTokenGrantCommand<DeviceCodeGrant>>,
                DefaultValidateDeviceCodeGrantHandler
            >();
            serviceCollection.TryAddEnumerable(validateDeviceCodeGrantHandler);

            // CIBA (Client-Initiated Backchannel Authentication)

            serviceCollection.TryAddSingleton<
                ICibaTokenResponseService,
                DefaultCibaTokenResponseService
            >();

            var cibaGrantHandler = ServiceDescriptor.Singleton<
                ITokenGrantHandler,
                DefaultCibaGrantHandler
            >();
            serviceCollection.TryAddEnumerable(cibaGrantHandler);

            var validateCibaGrantHandler = ServiceDescriptor.Singleton<
                ICommandHandler<ValidateTokenGrantCommand<CibaGrant>>,
                DefaultValidateCibaGrantHandler
            >();
            serviceCollection.TryAddEnumerable(validateCibaGrantHandler);

            // Client Credentials

            var clientCredentialsGrantHandler = ServiceDescriptor.Singleton<
                ITokenGrantHandler,
                DefaultClientCredentialsGrantHandler
            >();
            serviceCollection.TryAddEnumerable(clientCredentialsGrantHandler);

            var validateClientCredentialsGrantHandler = ServiceDescriptor.Singleton<
                ICommandHandler<ValidateTokenGrantCommand<ClientCredentialsGrant>>,
                DefaultValidateClientCredentialsGrantHandler
            >();
            serviceCollection.TryAddEnumerable(validateClientCredentialsGrantHandler);

            // Password

            var passwordGrantHandler = ServiceDescriptor.Singleton<
                ITokenGrantHandler,
                DefaultPasswordGrantHandler
            >();
            serviceCollection.TryAddEnumerable(passwordGrantHandler);

            // The password grant is unsupported until a host registers the local-account authentication capability
            // (the NCode.Identity.OpenId.Accounts.Authentication package), which replaces this default handler.
            serviceCollection.TryAddSingleton<
                ICommandResponseHandler<
                    AuthenticatePasswordGrantCommand,
                    AuthenticateSubjectDisposition
                >,
                DefaultUnsupportedPasswordGrantHandler
            >();

            var validatePasswordGrantHandler = ServiceDescriptor.Singleton<
                ICommandHandler<ValidateTokenGrantCommand<PasswordGrant>>,
                DefaultValidatePasswordGrantHandler
            >();
            serviceCollection.TryAddEnumerable(validatePasswordGrantHandler);

            // Token Exchange (RFC 8693)

            serviceCollection.TryAddSingleton<
                ISubjectTokenValidator,
                DefaultSubjectTokenValidator
            >();

            var tokenExchangeGrantHandler = ServiceDescriptor.Singleton<
                ITokenGrantHandler,
                DefaultTokenExchangeGrantHandler
            >();
            serviceCollection.TryAddEnumerable(tokenExchangeGrantHandler);

            var validateTokenExchangeGrantHandler = ServiceDescriptor.Singleton<
                ICommandHandler<ValidateTokenGrantCommand<TokenExchangeGrant>>,
                DefaultValidateTokenExchangeGrantHandler
            >();
            serviceCollection.TryAddEnumerable(validateTokenExchangeGrantHandler);
        }
    }
}
