// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Contracts;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class ControlPlanePrincipalsEndpointProviderTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IControlPlanePrincipalDirectory> MockDirectory { get; }

    public ControlPlanePrincipalsEndpointProviderTests()
    {
        MockDirectory = MockRepository.Create<IControlPlanePrincipalDirectory>();
    }

    private static WebApplication CreateApp(bool isControlPlane)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.Configure<CellOptions>(options => options.IsControlPlane = isControlPlane);
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
    public async Task Map_WhenTheCellIsNotTheControlPlane_MapsNothing()
    {
        await using var app = CreateApp(isControlPlane: false);

        var endpoints = MapEndpoints(app);

        Assert.Empty(endpoints);
    }

    #endregion

    #region FindAsync Tests

    [Fact]
    public async Task FindAsync_WhenTheQueryNamesTooMany_AnswersBadRequest()
    {
        var query = new ControlPlanePrincipalQuery
        {
            PrincipalIds = [.. Enumerable.Range(0, 60).Select(x => $"p{x}")],
            UserNames = [.. Enumerable.Range(0, 41).Select(x => $"u{x}")],
        };

        var result = await ControlPlanePrincipalsEndpointProvider.FindAsync(
            query,
            MockDirectory.Object,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task FindAsync_WhenTheQueryNamesTheMost_AnswersTheSummaries()
    {
        var query = new ControlPlanePrincipalQuery
        {
            PrincipalIds = [.. Enumerable.Range(0, 60).Select(x => $"p{x}")],
            UserNames = [.. Enumerable.Range(0, 40).Select(x => $"u{x}")],
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
}
