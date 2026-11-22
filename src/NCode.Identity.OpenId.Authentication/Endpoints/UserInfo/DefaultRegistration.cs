// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.OpenId.Authentication.Endpoints.UserInfo.Commands;
using NCode.Identity.OpenId.Authentication.Endpoints.UserInfo.Handlers;
using NCode.Identity.Registration.AspNetCore;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Authentication.Endpoints.UserInfo;

/// <summary>
/// Provides extension methods to configure services and handlers for the OpenId <c>UserInfo</c> endpoint.
/// </summary>
internal static class DefaultRegistration
{
    extension(IEndpointGroupBuilder builder)
    {
        /// <summary>
        /// Configures services and handlers for the OpenId <c>UserInfo</c> endpoint (OpenID Connect Core 5.3).
        /// </summary>
        public void AddUserInfoEndpoint()
        {
            builder.AddEndpoint<DefaultUserInfoEndpointProvider>();

            var claimsHandler = ServiceDescriptor.Singleton<
                ICommandHandler<GetUserInfoClaimsCommand>,
                DefaultGetUserInfoClaimsHandler
            >();
            builder.ServiceCollection.TryAddEnumerable(claimsHandler);

            var requestedClaimsHandler = ServiceDescriptor.Singleton<
                ICommandHandler<GetUserInfoClaimsCommand>,
                DefaultGetRequestedUserInfoClaimsHandler
            >();
            builder.ServiceCollection.TryAddEnumerable(requestedClaimsHandler);

            var metadataClaimsHandler = ServiceDescriptor.Singleton<
                ICommandHandler<GetUserInfoClaimsCommand>,
                DefaultGetUserInfoMetadataClaimsHandler
            >();
            builder.ServiceCollection.TryAddEnumerable(metadataClaimsHandler);
        }
    }
}
