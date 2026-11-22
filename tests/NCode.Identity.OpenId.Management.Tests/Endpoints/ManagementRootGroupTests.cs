// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace NCode.Identity.OpenId.Management.Endpoints;

public class ManagementRootGroupTests
{
    private static RouteEndpoint GetEndpoint(WebApplication app, string rawText)
    {
        var endpoints = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
        return Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == rawText);
    }

    #region ConfigureRoutes Tests

    [Fact]
    public async Task ConfigureRoutes_Always_LimitsARoutesRequestBodyToOneMebibyte()
    {
        await using var app = WebApplication.CreateSlimBuilder().Build();
        var group = app.MapGroup("/api");
        new ManagementRootGroup().ConfigureRoutes(group);
        group.MapPost("/ordinary", () => TypedResults.Ok());

        var endpoint = GetEndpoint(app, "/api/ordinary");

        Assert.Equal(
            1024 * 1024,
            endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize
        );
    }

    [Fact]
    public async Task ConfigureRoutes_WithARouteThatSetsItsOwnLimit_KeepsTheRoutesLimit()
    {
        await using var app = WebApplication.CreateSlimBuilder().Build();
        var group = app.MapGroup("/api");
        new ManagementRootGroup().ConfigureRoutes(group);
        group
            .MapPost("/large", () => TypedResults.Ok())
            .WithMetadata(new RequestSizeLimitAttribute(4L * 1024 * 1024));

        var endpoint = GetEndpoint(app, "/api/large");

        Assert.Equal(
            4L * 1024 * 1024,
            endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize
        );
    }

    #endregion
}
