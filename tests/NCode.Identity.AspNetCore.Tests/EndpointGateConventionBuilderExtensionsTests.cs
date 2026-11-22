// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NCode.Buffers;
using Xunit;

namespace NCode.Identity.AspNetCore.Tests;

public class EndpointGateConventionBuilderExtensionsTests
{
    private static async ValueTask<WebApplication> CreateAppAsync(
        Action<IServiceCollection>? configureServices,
        Action<WebApplication> mapRoutes,
        CancellationToken cancellationToken = default
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        mapRoutes(app);

        await app.StartAsync(cancellationToken);

        return app;
    }

    private static async ValueTask<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        string body,
        CancellationToken cancellationToken = default
    )
    {
        using var content = new StringContent(body, SecureEncoding.UTF8, "application/json");
        return await client.PostAsync(path, content, cancellationToken);
    }

    private static Ok HandleRequest(JsonElement payload) => TypedResults.Ok();

    #region AddEndpointGate Tests

    [Fact]
    public async Task AddEndpointGate_WhenTheGateRefuses_AnswersBeforeTheBodyBinds()
    {
        await using var app = await CreateAppAsync(
            configureServices: null,
            mapRoutes: webApplication =>
            {
                var route = webApplication.MapPost("/gated", HandleRequest);
                route.AddEndpointGate<RouteHandlerBuilder, RefusingGate>();
            }
        );
        using var client = app.GetTestClient();

        using var response = await PostAsync(client, "/gated", "{");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddEndpointGate_WhenTheGateLetsTheRequestThrough_RunsTheEndpoint()
    {
        await using var app = await CreateAppAsync(
            configureServices: null,
            mapRoutes: webApplication =>
            {
                var route = webApplication.MapPost("/gated", HandleRequest);
                route.AddEndpointGate<RouteHandlerBuilder, PassingGate>();
            }
        );
        using var client = app.GetTestClient();

        using var response = await PostAsync(client, "/gated", """{"value":"a"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddEndpointGate_OnAGroupAndItsRoute_RunsTheGroupsGateFirst()
    {
        var calls = new List<string>();
        await using var app = await CreateAppAsync(
            configureServices: serviceCollection => serviceCollection.AddSingleton(calls),
            mapRoutes: webApplication =>
            {
                var group = webApplication.MapGroup("/group");
                group.AddEndpointGate<RouteGroupBuilder, GroupGate>();

                var route = group.MapPost("/gated", HandleRequest);
                route.AddEndpointGate<RouteHandlerBuilder, RouteGate>();
            }
        );
        using var client = app.GetTestClient();

        using var response = await PostAsync(client, "/group/gated", """{"value":"a"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["group", "route"], calls);
    }

    #endregion

    public sealed class RefusingGate : IEndpointGate
    {
        public ValueTask<IResult?> InvokeAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken
        )
        {
            IResult? result = TypedResults.StatusCode(StatusCodes.Status401Unauthorized);
            return ValueTask.FromResult<IResult?>(result);
        }
    }

    public sealed class PassingGate : IEndpointGate
    {
        public ValueTask<IResult?> InvokeAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<IResult?>(null);
    }

    public sealed class GroupGate(List<string> calls) : IEndpointGate
    {
        public ValueTask<IResult?> InvokeAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken
        )
        {
            calls.Add("group");
            return ValueTask.FromResult<IResult?>(null);
        }
    }

    public sealed class RouteGate(List<string> calls) : IEndpointGate
    {
        public ValueTask<IResult?> InvokeAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken
        )
        {
            calls.Add("route");
            return ValueTask.FromResult<IResult?>(null);
        }
    }
}
