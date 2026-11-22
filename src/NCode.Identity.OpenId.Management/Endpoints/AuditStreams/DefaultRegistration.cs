// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.Endpoints.AuditStreams;

/// <summary>
/// Provides extension methods to configure services and handlers for the management audit streams endpoints.
/// </summary>
internal static class DefaultRegistration
{
    extension(IEndpointGroupBuilder builder)
    {
        /// <summary>
        /// Registers the management audit streams endpoints.
        /// </summary>
        public void AddAuditStreamEndpoint()
        {
            builder.AddEndpoint<AuditStreamApiEndpointHandler>();
        }
    }
}
