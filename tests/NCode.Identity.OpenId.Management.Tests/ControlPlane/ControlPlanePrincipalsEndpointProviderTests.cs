// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NCode.Buffers;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class ControlPlanePrincipalsEndpointProviderTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IOpenIdContextFactory> MockContextFactory { get; }

    private Mock<IMediator> MockMediator { get; }

    private Mock<IControlPlanePrincipalDirectory> MockDirectory { get; }

    public ControlPlanePrincipalsEndpointProviderTests()
    {
        MockContextFactory = MockRepository.Create<IOpenIdContextFactory>();
        MockMediator = MockRepository.Create<IMediator>();
        MockDirectory = MockRepository.Create<IControlPlanePrincipalDirectory>();
    }

    private WebApplication CreateApp(bool isControlPlane)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.Configure<CellOptions>(options => options.IsControlPlane = isControlPlane);
        builder.Services.AddSingleton(MockContextFactory.Object);
        builder.Services.AddSingleton(MockMediator.Object);
        builder.Services.AddSingleton(MockDirectory.Object);
        return builder.Build();
    }

    private static List<RouteEndpoint> MapEndpoints(WebApplication app)
    {
        new ControlPlanePrincipalsEndpointProvider().Map(app);

        return ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    #region Map Tests

    [Fact]
    public async Task Map_WhenTheCellIsTheControlPlane_MapsThePostRouteHiddenFromTheDescription()
    {
        await using var app = CreateApp(isControlPlane: true);

        var endpoints = MapEndpoints(app);

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("/control-plane/v1/principals", endpoint.RoutePattern.RawText);
        var methods = Assert.IsAssignableFrom<IHttpMethodMetadata>(
            endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()
        );
        Assert.Equal([HttpMethods.Post], methods.HttpMethods);
        var exclude = endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>();
        Assert.True(exclude?.ExcludeFromDescription);
    }

    [Fact]
    public async Task Map_WhenTheCellIsTheControlPlane_TheRouteRunsTheAuthenticationGateBeforeTheDirectory()
    {
        await using var app = CreateApp(isControlPlane: true);
        var endpoint = Assert.Single(MapEndpoints(app));
        var refusal = TypedResults.NotFound();
        MockContextFactory
            .Setup(x =>
                x.CreateForControlPlaneAsync(
                    It.IsAny<HttpContext>(),
                    MockMediator.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(refusal.AsException("x"))
            .Verifiable();
        var httpContext = new DefaultHttpContext { RequestServices = app.Services };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        var body = SecureEncoding.UTF8.GetBytes("""{"principalIds":["p1"]}""");
        httpContext.Request.Body = new MemoryStream(body);
        httpContext.Request.ContentLength = body.Length;
        httpContext.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetectionFeature());
        httpContext.Response.Body = new MemoryStream();
        httpContext.SetEndpoint(endpoint);

        var requestDelegate =
            endpoint.RequestDelegate
            ?? throw new InvalidOperationException("The endpoint has no request delegate.");

        await requestDelegate(httpContext);

        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Map_WhenTheCellIsNotTheControlPlane_MapsNothing()
    {
        await using var app = CreateApp(isControlPlane: false);

        var endpoints = MapEndpoints(app);

        Assert.Empty(endpoints);
    }

    #endregion

    #region FindAsync Tests

    private const int HalfOfTheMost = ControlPlanePrincipalQuery.MaxNames / 2;

    private static string[] Names(string prefix, int count) =>
        [.. Enumerable.Range(0, count).Select(x => $"{prefix}{x}")];

    private async ValueTask AssertBadRequestAsync(
        ControlPlanePrincipalQuery query,
        string detail,
        CancellationToken cancellationToken
    )
    {
        var result = await ControlPlanePrincipalsEndpointProvider.FindAsync(
            query,
            MockDirectory.Object,
            cancellationToken
        );

        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(detail, problem.ProblemDetails.Detail);
    }

    private const string BlankNamesDetail =
        "A query's 'principalIds' and 'userNames' are lists of non-blank names.";

    [Fact]
    public async Task FindAsync_WhenTheQueryNamesTooMany_AnswersBadRequest()
    {
        var query = new ControlPlanePrincipalQuery
        {
            PrincipalIds = Names("p", HalfOfTheMost),
            UserNames = Names("u", ControlPlanePrincipalQuery.MaxNames - HalfOfTheMost + 1),
        };

        await AssertBadRequestAsync(
            query,
            $"A query names at most {ControlPlanePrincipalQuery.MaxNames} principal identifiers and user names in all.",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task FindAsync_WhenThePrincipalIdsAreNull_AnswersBadRequest()
    {
        var query = new ControlPlanePrincipalQuery { PrincipalIds = null! };

        await AssertBadRequestAsync(query, BlankNamesDetail, CancellationToken.None);
    }

    [Fact]
    public async Task FindAsync_WhenTheUserNamesAreNull_AnswersBadRequest()
    {
        var query = new ControlPlanePrincipalQuery { UserNames = null! };

        await AssertBadRequestAsync(query, BlankNamesDetail, CancellationToken.None);
    }

    [Fact]
    public async Task FindAsync_WhenAPrincipalIdIsNull_AnswersBadRequest()
    {
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1", null!] };

        await AssertBadRequestAsync(query, BlankNamesDetail, CancellationToken.None);
    }

    [Fact]
    public async Task FindAsync_WhenAUserNameIsBlank_AnswersBadRequest()
    {
        var query = new ControlPlanePrincipalQuery { UserNames = ["u1", "  "] };

        await AssertBadRequestAsync(query, BlankNamesDetail, CancellationToken.None);
    }

    [Fact]
    public async Task FindAsync_WhenTheQueryNamesTooManyAndOneIsBlank_AnswersTheBlankNameDetail()
    {
        var query = new ControlPlanePrincipalQuery
        {
            PrincipalIds = [.. Names("p", HalfOfTheMost), ""],
            UserNames = Names("u", ControlPlanePrincipalQuery.MaxNames - HalfOfTheMost + 1),
        };

        await AssertBadRequestAsync(query, BlankNamesDetail, CancellationToken.None);
    }

    [Fact]
    public async Task FindAsync_WhenTheQueryNamesTheMost_AnswersTheSummaries()
    {
        var query = new ControlPlanePrincipalQuery
        {
            PrincipalIds = Names("p", HalfOfTheMost),
            UserNames = Names("u", ControlPlanePrincipalQuery.MaxNames - HalfOfTheMost),
        };
        IReadOnlyList<PrincipalSummaryResource> summaries = [];
        MockDirectory
            .Setup(x => x.FindAsync(query, CancellationToken.None))
            .Returns(new ValueTask<IReadOnlyList<PrincipalSummaryResource>>(summaries))
            .Verifiable();

        var result = await ControlPlanePrincipalsEndpointProvider.FindAsync(
            query,
            MockDirectory.Object,
            CancellationToken.None
        );

        var ok = Assert.IsType<Ok<CollectionResource<PrincipalSummaryResource>>>(result.Result);
        Assert.Same(summaries, ok.Value?.Items);
    }

    #endregion

    private sealed class BodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
