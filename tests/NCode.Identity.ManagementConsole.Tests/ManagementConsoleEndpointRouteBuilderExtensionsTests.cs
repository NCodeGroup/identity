// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NCode.Identity.Bff;
using NCode.Identity.ManagementConsole.Options;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace NCode.Identity.ManagementConsole.Tests;

public class ManagementConsoleEndpointRouteBuilderExtensionsTests : BaseTests
{
    private const string ManagementApiUrl = "https://id.example.com/api";
    private const string EuCellUrl = "https://eu-1.example.com/api";
    private const string UsCellUrl = "https://us-2.example.com/api";

    private Mock<IBffTokenAccessor> MockTokens { get; }

    private Mock<IHttpForwarder> MockForwarder { get; }

    public ManagementConsoleEndpointRouteBuilderExtensionsTests()
    {
        MockTokens = CreateStrictMock<IBffTokenAccessor>();
        MockForwarder = CreateStrictMock<IHttpForwarder>();
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ManagementConsole:ManagementApiUrl"] = ManagementApiUrl,
                    ["ManagementConsole:BundleRootPath"] = "bundle",
                    ["Bff:Frontends:mgmt:Authority"] = "https://id.example.com",
                    ["Bff:Frontends:mgmt:PublicOrigin"] = "https://console.example.com",
                    ["Bff:Frontends:mgmt:ClientId"] = "mgmt-console",
                    ["Bff:Frontends:mgmt:ReturnUrls:0"] = "/",
                }
            )
            .Build();

    private WebApplication CreateApp(bool withCells)
    {
        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );
        var serviceCollection = builder.Services;
        serviceCollection.AddManagementConsole(CreateConfiguration());
        if (withCells)
        {
            // Configured in code and out of ordinal order: configuration hands the binder its keys sorted.
            serviceCollection.Configure<ManagementConsoleOptions>(options =>
            {
                options.Cells["us-2"] = UsCellUrl;
                options.Cells["eu-1"] = EuCellUrl;
            });
        }

        // Strict mocks, so no forwarder reaches a token service or the network.
        serviceCollection.AddSingleton(MockTokens.Object);
        serviceCollection.AddSingleton(MockForwarder.Object);
        return builder.Build();
    }

    private static List<RouteEndpoint> GetForwarders(IEndpointRouteBuilder endpoints) =>
        endpoints
            .DataSources.SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(x =>
                x.RoutePattern.RawText is { } pattern
                && pattern.StartsWith(
                    ManagementConsoleDefaults.ApiBasePath + "/",
                    StringComparison.Ordinal
                )
            )
            .ToList();

    #region MapManagementConsole Tests

    [Fact]
    public async Task MapManagementConsole_WithCells_MapsAForwarderPerCellInOrdinalOrder()
    {
        await using var app = CreateApp(withCells: true);

        app.MapManagementConsole();

        var patterns = GetForwarders(app).Select(x => x.RoutePattern.RawText).ToList();
        Assert.Equal(
            [
                "/management/api/{**remainder}",
                "/management/cells/eu-1/api/{**remainder}",
                "/management/cells/us-2/api/{**remainder}",
            ],
            patterns
        );
    }

    [Fact]
    public async Task MapManagementConsole_WithCells_LimitsEveryForwarderToTheConsoleFrontend()
    {
        await using var app = CreateApp(withCells: true);

        app.MapManagementConsole();

        var forwarders = GetForwarders(app);
        Assert.Equal(3, forwarders.Count);
        Assert.All(
            forwarders,
            x =>
            {
                Assert.NotNull(x.Metadata.GetMetadata<BffApiAttribute>());
                Assert.Equal(
                    ManagementConsoleDefaults.FrontendName,
                    Assert.Single(x.Metadata.GetOrderedMetadata<BffFrontendAttribute>()).Name
                );
            }
        );
    }

    [Fact]
    public async Task MapManagementConsole_WithoutCells_MapsOnlyTheManagementApiForwarder()
    {
        await using var app = CreateApp(withCells: false);

        app.MapManagementConsole();

        Assert.Equal(
            "/management/api/{**remainder}",
            Assert.Single(GetForwarders(app)).RoutePattern.RawText
        );
    }

    [Theory]
    [InlineData("/management/api/tenants", ManagementApiUrl)]
    [InlineData("/management/cells/eu-1/api/tenants", EuCellUrl)]
    [InlineData("/management/cells/us-2/api/tenants", UsCellUrl)]
    public async Task MapManagementConsole_RequestUnderAForwarderPath_ForwardsToItsUrlWithTheUsersManagementToken(
        string path,
        string destination
    )
    {
        await using var app = CreateApp(withCells: true);
        app.MapManagementConsole();
        app.UseRouting();
        // Runs the endpoint that routing selects, as the endpoint middleware does.
        app.Use(
            static (HttpContext httpContext, RequestDelegate _) =>
                httpContext.GetEndpoint()?.RequestDelegate?.Invoke(httpContext)
                ?? Task.CompletedTask
        );
        var pipeline = ((IApplicationBuilder)app).Build();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        MockTokens
            .Setup(x =>
                x.GetAccessTokenAsync(
                    context,
                    It.Is<BffTokenRequest>(request =>
                        request.Requirement == BffTokenRequirementNames.User
                        && request.Resource == ManagementConsoleDefaults.ManagementApiResource
                        && request.Scope == null
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(BffTokenResult.Success(null))
            .Verifiable();
        MockForwarder
            .Setup(x =>
                x.SendAsync(
                    context,
                    destination,
                    It.IsAny<HttpMessageInvoker>(),
                    It.IsAny<ForwarderRequestConfig>(),
                    It.IsAny<HttpTransformer>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(ForwarderError.None)
            .Verifiable();

        await pipeline(context);
    }

    #endregion
}
