// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.OpenId.Management.Endpoints;

public class ManagementFailureEndpointFilterTests
{
    private static (
        ManagementFailureEndpointFilter Filter,
        EndpointFilterInvocationContext Context
    ) Create()
    {
        var httpContext = new DefaultHttpContext();

        return (
            new ManagementFailureEndpointFilter(),
            EndpointFilterInvocationContext.Create(httpContext)
        );
    }

    #region InvokeAsync Tests

    [Fact]
    public async Task InvokeAsync_WhenBindingFailed_ReturnsEmptyWithoutCallingTheRoute()
    {
        var (filter, context) = Create();
        context.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        EndpointFilterDelegate next = _ =>
            throw new InvalidOperationException("The route must not run.");

        var result = await filter.InvokeAsync(context, next);

        Assert.Same(TypedResults.Empty, result);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheRouteThrowsAnHttpResult_ReturnsItsResult()
    {
        var (filter, context) = Create();
        var refusal = TypedResults.NotFound();
        EndpointFilterDelegate next = _ =>
            ValueTask.FromException<object?>(refusal.AsException("x"));

        var result = await filter.InvokeAsync(context, next);

        Assert.Same(refusal, result);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheControlPlaneIsUnavailable_Answers503WithRetryAfter()
    {
        var (filter, context) = Create();
        EndpointFilterDelegate next = _ =>
            throw new DirectoryUnavailableException("The control plane is unavailable.");

        var result = await filter.InvokeAsync(context, next);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal(
            $"{GlobalDirectoryProtocol.UnavailableRetryAfterSeconds}",
            context.HttpContext.Response.Headers.RetryAfter.ToString()
        );
    }

    [Fact]
    public async Task InvokeAsync_WhenTheRouteSucceeds_ReturnsItsResult()
    {
        var (filter, context) = Create();
        var expected = TypedResults.NoContent();
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(expected);

        var result = await filter.InvokeAsync(context, next);

        Assert.Same(expected, result);
    }

    #endregion
}
