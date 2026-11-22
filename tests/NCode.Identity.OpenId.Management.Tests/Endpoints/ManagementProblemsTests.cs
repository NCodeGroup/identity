// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;

namespace NCode.Identity.OpenId.Management.Endpoints;

public class ManagementProblemsTests
{
    #region AuthenticationRequired Tests

    [Fact]
    public void AuthenticationRequired_WithoutAnHttpContext_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ManagementProblems.AuthenticationRequired(null!)
        );
    }

    [Theory]
    [InlineData(null, new[] { "Bearer", "DPoP" })]
    [InlineData("Basic abc", new[] { "Bearer", "DPoP" })]
    [InlineData("Bearer abc", new[] { "Bearer error=\"invalid_token\"", "DPoP" })]
    [InlineData("bearer abc", new[] { "Bearer error=\"invalid_token\"", "DPoP" })]
    [InlineData("DPoP abc", new[] { "Bearer", "DPoP error=\"invalid_token\"" })]
    public void AuthenticationRequired_Always_ReturnsAnUnauthorizedProblemWithOneChallengePerScheme(
        string? authorization,
        string[] expectedChallenges
    )
    {
        var httpContext = new DefaultHttpContext();
        if (authorization is not null)
            httpContext.Request.Headers.Authorization = authorization;

        var result = ManagementProblems.AuthenticationRequired(httpContext);

        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.ProblemDetails.Status);
        Assert.Equal("Authentication is required.", result.ProblemDetails.Detail);
        Assert.Equal(expectedChallenges, httpContext.Response.Headers.WWWAuthenticate.ToArray());
    }

    #endregion

    #region ResourceNotFound Tests

    [Fact]
    public void ResourceNotFound_Always_ReturnsANotFoundProblem()
    {
        var result = ManagementProblems.ResourceNotFound();

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, result.ProblemDetails.Status);
        Assert.Equal("The resource does not exist.", result.ProblemDetails.Detail);
    }

    #endregion
}
