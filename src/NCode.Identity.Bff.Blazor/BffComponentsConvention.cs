// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;

namespace NCode.Identity.Bff.Blazor;

/// <summary>
/// Marks the endpoints of a Razor Components host as BFF endpoints.
/// </summary>
internal static class BffComponentsConvention
{
    /// <summary>
    /// Marks a component page as a BFF page and the Blazor hub as a BFF hub that admits anonymous circuits and
    /// revalidates its own connections. Every other endpoint carries no session and no token, so it is left untouched.
    /// </summary>
    /// <param name="endpointBuilder">The endpoint to mark.</param>
    public static void Apply(EndpointBuilder endpointBuilder)
    {
        ArgumentNullException.ThrowIfNull(endpointBuilder);

        var metadata = endpointBuilder.Metadata;
        if (metadata.OfType<ComponentTypeMetadata>().Any())
        {
            var pageAttribute = new BffPageAttribute();
            metadata.Add(pageAttribute);
            metadata.Add(BffBlazorLibraryRequirement.Instance);
        }
        else if (metadata.OfType<HubMetadata>().Any() || metadata.OfType<NegotiateMetadata>().Any())
        {
            var hubAttribute = new BffHubAttribute();
            metadata.Add(hubAttribute);
            var allowAnonymousAttribute = new AllowAnonymousAttribute();
            metadata.Add(allowAnonymousAttribute);
            var selfRevalidatingHubAttribute = new BffSelfRevalidatingHubAttribute();
            metadata.Add(selfRevalidatingHubAttribute);
            metadata.Add(BffBlazorLibraryRequirement.Instance);
        }
    }
}
