// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class ManagementAuthorizationHeaderTests
{
    private static DefaultHttpContext CreateContext(string? authorization)
    {
        var httpContext = new DefaultHttpContext();
        if (authorization is not null)
            httpContext.Request.Headers.Authorization = authorization;

        return httpContext;
    }

    [Theory]
    [InlineData(OpenIdProtocol.TokenTypes.DPoP)]
    [InlineData(OpenIdProtocol.TokenTypes.Bearer)]
    public void TryReadToken_WhenTheHeaderNamesTheScheme_ReturnsTheToken(string scheme)
    {
        var httpContext = CreateContext($"{scheme} a.b.c");

        var result = ManagementAuthorizationHeader.TryReadToken(httpContext, scheme, out var token);

        Assert.True(result);
        Assert.Equal("a.b.c", token);
    }

    [Theory]
    [InlineData("dpop a.b.c")]
    [InlineData("DPOP a.b.c")]
    public void TryReadToken_WhenTheSchemeDiffersInCase_ReturnsTheToken(string authorization)
    {
        var httpContext = CreateContext(authorization);

        var result = ManagementAuthorizationHeader.TryReadToken(
            httpContext,
            OpenIdProtocol.TokenTypes.DPoP,
            out var token
        );

        Assert.True(result);
        Assert.Equal("a.b.c", token);
    }

    [Fact]
    public void TryReadToken_WhenTheTokenHasSurroundingSpace_ReturnsTheTrimmedToken()
    {
        var httpContext = CreateContext("DPoP   a.b.c  ");

        var result = ManagementAuthorizationHeader.TryReadToken(
            httpContext,
            OpenIdProtocol.TokenTypes.DPoP,
            out var token
        );

        Assert.True(result);
        Assert.Equal("a.b.c", token);
    }

    [Fact]
    public void TryReadToken_WhenTheHeaderIsMissing_ReturnsFalse()
    {
        var httpContext = CreateContext(null);

        var result = ManagementAuthorizationHeader.TryReadToken(
            httpContext,
            OpenIdProtocol.TokenTypes.DPoP,
            out var token
        );

        Assert.False(result);
        Assert.Null(token);
    }

    [Fact]
    public void TryReadToken_WhenTheHeaderNamesAnotherScheme_ReturnsFalse()
    {
        var httpContext = CreateContext("Bearer a.b.c");

        var result = ManagementAuthorizationHeader.TryReadToken(
            httpContext,
            OpenIdProtocol.TokenTypes.DPoP,
            out var token
        );

        Assert.False(result);
        Assert.Null(token);
    }

    [Theory]
    [InlineData("DPoP")]
    [InlineData("DPoP ")]
    [InlineData("DPoP    ")]
    [InlineData("DPoPa.b.c")]
    public void TryReadToken_WhenTheTokenIsEmptyOrNotSeparated_ReturnsFalse(string authorization)
    {
        var httpContext = CreateContext(authorization);

        var result = ManagementAuthorizationHeader.TryReadToken(
            httpContext,
            OpenIdProtocol.TokenTypes.DPoP,
            out var token
        );

        Assert.False(result);
        Assert.Null(token);
    }
}
