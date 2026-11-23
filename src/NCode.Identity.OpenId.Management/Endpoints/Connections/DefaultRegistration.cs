// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection.Extensions;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.Endpoints.Connections;

/// <summary>
/// Provides extension methods to configure services and handlers for the management connections endpoints.
/// </summary>
internal static class DefaultRegistration
{
    extension(IEndpointGroupBuilder builder)
    {
        /// <summary>
        /// Registers the management connections endpoints and the services they depend on.
        /// </summary>
        public void AddConnectionEndpoint()
        {
            builder.AddEndpoint<ConnectionApiEndpointHandler>();
            builder.ServiceCollection.TryAddSingleton<
                IConnectionValidator,
                DefaultConnectionValidator
            >();
        }
    }
}
