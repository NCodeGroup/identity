// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace NCode.Identity.AspNetCore.Tests;

public class RetryAfterProblemTests : BaseTests
{
    #region GetWaitSeconds Tests

    [Theory]
    [InlineData(90.2, 91L)]
    [InlineData(5.0, 5L)]
    [InlineData(0.01, 1L)]
    [InlineData(0.0, 1L)]
    [InlineData(-5.0, 1L)]
    public void GetWaitSeconds_Always_RoundsUpToWholeSecondsOfAtLeastOne(
        double waitSeconds,
        long expected
    )
    {
        Assert.Equal(expected, RetryAfterProblem.GetWaitSeconds(TimeSpan.FromSeconds(waitSeconds)));
    }

    #endregion

    #region Create Tests

    [Fact]
    public void Create_WhenTheWaitIsFractional_RoundsUpToWholeSecondsInHeaderAndMember()
    {
        var httpContext = new DefaultHttpContext();

        var result = RetryAfterProblem.Create(
            httpContext,
            TimeSpan.FromSeconds(90.2),
            new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Detail = "Wait for the replicas.",
            }
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("Wait for the replicas.", problem.ProblemDetails.Detail);
        Assert.Equal("91", httpContext.Response.Headers.RetryAfter.ToString());
        Assert.Equal(91L, problem.ProblemDetails.Extensions[RetryAfterProblem.MemberName]);
    }

    [Fact]
    public void Create_WhenTheWaitIsUnderOneSecond_WaitsOneSecond()
    {
        var httpContext = new DefaultHttpContext();

        var result = RetryAfterProblem.Create(
            httpContext,
            TimeSpan.FromMilliseconds(10),
            new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable }
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal("1", httpContext.Response.Headers.RetryAfter.ToString());
        Assert.Equal(1L, problem.ProblemDetails.Extensions[RetryAfterProblem.MemberName]);
    }

    [Fact]
    public void Create_WhenTheProblemHasAnExtension_KeepsIt()
    {
        var httpContext = new DefaultHttpContext();
        var details = new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable };
        details.Extensions["resource"] = "r-1";

        var result = RetryAfterProblem.Create(httpContext, TimeSpan.FromSeconds(5), details);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal("r-1", problem.ProblemDetails.Extensions["resource"]);
        Assert.Equal(5L, problem.ProblemDetails.Extensions[RetryAfterProblem.MemberName]);
    }

    [Fact]
    public void Create_WhenTheHttpContextIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            "httpContext",
            () => RetryAfterProblem.Create(null!, TimeSpan.FromSeconds(1), new ProblemDetails())
        );
    }

    [Fact]
    public void Create_WhenTheProblemIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            "problem",
            () => RetryAfterProblem.Create(new DefaultHttpContext(), TimeSpan.FromSeconds(1), null!)
        );
    }

    #endregion
}
