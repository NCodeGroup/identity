// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class CellDirectoryConflictEndpointFilterTests
{
    private static DefaultEndpointFilterInvocationContext CreateContext() =>
        new(new DefaultHttpContext(), []);

    [Fact]
    public async Task InvokeAsync_WhenTheHandlerThrowsAConflict_AnswersAConflictProblemWithTheMessage()
    {
        var filter = new CellDirectoryConflictEndpointFilter();

        var result = await filter.InvokeAsync(
            CreateContext(),
            _ => throw new CellDirectoryConflictException("The cell is retired.")
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("Conflict", problem.ProblemDetails.Title);
        Assert.Equal("The cell is retired.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheHandlerSucceeds_ReturnsItsResult()
    {
        var filter = new CellDirectoryConflictEndpointFilter();
        var expected = TypedResults.NoContent();

        var result = await filter.InvokeAsync(
            CreateContext(),
            _ => ValueTask.FromResult<object?>(expected)
        );

        Assert.Same(expected, result);
    }
}
