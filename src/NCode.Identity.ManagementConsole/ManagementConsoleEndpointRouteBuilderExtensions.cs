// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore;
using NCode.Identity.Bff;
using NCode.Identity.Bff.Endpoints;
using NCode.Identity.ManagementConsole.Options;

namespace NCode.Identity.ManagementConsole;

/// <summary>
/// Provides the ability to map the management console's endpoints onto the HTTP request pipeline.
/// </summary>
[PublicAPI]
public static class ManagementConsoleEndpointRouteBuilderExtensions
{
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> instance to map the endpoints to.</param>
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the BFF endpoints and remote APIs of the configured frontends, the management API forwarder under
        /// <see cref="ManagementConsoleDefaults.ApiPath"/>, one management API forwarder per configured cell under
        /// <c>{<see cref="ManagementConsoleDefaults.CellsPath"/>}/{cell}/api</c>, the console's bootstrap document under
        /// <see cref="ManagementConsoleDefaults.BootstrapPath"/>, the bundle's static assets under
        /// <see cref="ManagementConsoleDefaults.AssetsPath"/> and, as the fallback of every other
        /// <c>GET</c> and <c>HEAD</c> path that is not a file, the console's page.
        /// </summary>
        /// <remarks>
        /// Each management API forwarder is limited to the <see cref="ManagementConsoleDefaults.FrontendName"/>
        /// frontend, requires the <see cref="BffTokenRequirementNames.User"/> token requirement and requests the
        /// <see cref="ManagementConsoleDefaults.ManagementApiResource"/> resource. The cell forwarders, mapped in
        /// ordinal order of the cell names from <c>ManagementConsole:Cells</c>, are siblings of the
        /// <see cref="ManagementConsoleDefaults.ApiPath"/> forwarder and never its children, because the BFF maps a
        /// request path to one remote API.
        /// </remarks>
        /// <returns>The <see cref="RouteGroupBuilder"/> of the bootstrap, asset and page endpoints, which is excluded
        /// from the API description.</returns>
        /// <exception cref="InvalidOperationException">
        /// <c>ManagementConsole:ManagementApiUrl</c> is not set, or a remote API is invalid.
        /// </exception>
        public RouteGroupBuilder MapManagementConsole()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapBffEndpoints();
            endpoints.MapBffRemoteApis();

            var options = endpoints
                .ServiceProvider.GetRequiredService<IOptions<ManagementConsoleOptions>>()
                .Value;
            var managementApiUrl =
                options.ManagementApiUrl
                ?? throw new InvalidOperationException(
                    "ManagementConsole:ManagementApiUrl must be set."
                );

            endpoints
                .MapBffRemoteApi(
                    ManagementConsoleDefaults.ApiPath,
                    managementApiUrl,
                    api =>
                    {
                        api.TokenRequirement = BffTokenRequirementNames.User;
                        api.Resource = ManagementConsoleDefaults.ManagementApiResource;
                    }
                )
                .RequireBffFrontend(ManagementConsoleDefaults.FrontendName);

            foreach (
                var (cellName, cellUrl) in options.Cells.OrderBy(
                    static pair => pair.Key,
                    StringComparer.Ordinal
                )
            )
            {
                endpoints
                    .MapBffRemoteApi(
                        $"{ManagementConsoleDefaults.CellsPath}/{cellName}/api",
                        cellUrl,
                        api =>
                        {
                            api.TokenRequirement = BffTokenRequirementNames.User;
                            api.Resource = ManagementConsoleDefaults.ManagementApiResource;
                        }
                    )
                    .RequireBffFrontend(ManagementConsoleDefaults.FrontendName);
            }

            var group = endpoints.MapGroup(string.Empty).AllowAnonymous();

            group
                .MapGet(
                    ManagementConsoleDefaults.BootstrapPath,
                    static (HttpContext context) =>
                        context
                            .RequestServices.GetRequiredService<ManagementConsoleBootstrapHandler>()
                            .Handle(context)
                )
                .WithName("ManagementConsole.Bootstrap");

            group
                .MapMethods(
                    $"{ManagementConsoleDefaults.AssetsPath}/{{**asset}}",
                    [HttpMethods.Get, HttpMethods.Head],
                    static (HttpContext context, string? asset) =>
                        context
                            .RequestServices.GetRequiredService<
                                SinglePageBundle<ManagementConsolePage>
                            >()
                            .ServeAsset(context.Response, asset ?? string.Empty)
                )
                .WithName("ManagementConsole.Asset");

            group
                .MapFallback(
                    "{*path:nonfile}",
                    (Delegate)(
                        static (HttpContext context) =>
                            context
                                .RequestServices.GetRequiredService<ManagementConsolePageHandler>()
                                .HandleAsync(context, context.RequestAborted)
                    )
                )
                .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]))
                .WithName("ManagementConsole.Page");

            group.ExcludeFromDescription();

            return group;
        }
    }
}
