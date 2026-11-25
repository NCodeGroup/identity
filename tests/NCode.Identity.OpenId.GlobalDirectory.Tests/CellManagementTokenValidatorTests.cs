// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Buffers;
using NCode.Identity.OpenId.GlobalDirectory.ControlPlane;
using NCode.Identity.OpenId.Management.ControlPlane;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class CellManagementTokenValidatorTests : BaseTests
{
    private const string ControlPlaneIssuer = "https://control-plane.example.test";
    private const string CellIssuer = "https://cell-a.example.test/tenants/management";

    private Mock<IManagementTokenValidator> ControlPlaneMock { get; }
    private Mock<IManagementTokenValidator> CellMock { get; }

    public CellManagementTokenValidatorTests()
    {
        ControlPlaneMock = CreateStrictMock<IManagementTokenValidator>();
        CellMock = CreateStrictMock<IManagementTokenValidator>();
    }

    private CellManagementTokenValidator CreateValidator() =>
        new(
            ControlPlaneMock.Object,
            CellMock.Object,
            Options.Create(
                new CellOptions
                {
                    Name = "cell-a",
                    ControlPlane = new ControlPlaneOptions { Issuer = ControlPlaneIssuer },
                }
            )
        );

    private static string Base64Url(string text) =>
        Convert
            .ToBase64String(SecureEncoding.UTF8.GetBytes(text))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static DefaultHttpContext CreateHttpContext(string? issuer, string scheme = "DPoP")
    {
        var httpContext = new DefaultHttpContext();
        if (issuer is not null)
        {
            var header = Base64Url("""{"alg":"ES256"}""");
            var payload = Base64Url($$"""{"iss":"{{issuer}}"}""");
            httpContext.Request.Headers.Authorization = $"{scheme} {header}.{payload}.signature";
        }

        return httpContext;
    }

    private static ClaimsPrincipal CreatePrincipal() =>
        new(new ClaimsIdentity([new Claim("sub", "user-1")], "test"));

    [Fact]
    public async Task ValidateAsync_ControlPlaneIssuer_UsesReplicatedValidatorAndSetsClaim()
    {
        var httpContext = CreateHttpContext(ControlPlaneIssuer);
        var principal = CreatePrincipal();
        ControlPlaneMock
            .Setup(x => x.ValidateAsync(httpContext, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ManagementTokenValidation.Authenticated(principal))
            .Verifiable(Times.Once);

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        Assert.True(result.IsAuthenticated);
        Assert.Equal(
            ManagementIssuerKinds.ControlPlane,
            result.Principal!.FindFirst(ManagementIssuerKinds.ClaimType)?.Value
        );
    }

    [Fact]
    public async Task ValidateAsync_OtherIssuer_UsesCellValidatorAndSetsClaim()
    {
        var httpContext = CreateHttpContext(CellIssuer);
        var principal = CreatePrincipal();
        CellMock
            .Setup(x => x.ValidateAsync(httpContext, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ManagementTokenValidation.Authenticated(principal))
            .Verifiable(Times.Once);

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        Assert.True(result.IsAuthenticated);
        var claims = result.Principal!.FindAll(ManagementIssuerKinds.ClaimType).ToArray();
        Assert.Single(claims);
        Assert.Equal(ManagementIssuerKinds.Cell, claims[0].Value);
    }

    [Fact]
    public async Task ValidateAsync_UnknownIssuerRefusedByCellValidator_IsUnauthenticated()
    {
        var httpContext = CreateHttpContext("https://other.example.test");
        CellMock
            .Setup(x => x.ValidateAsync(httpContext, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ManagementTokenValidation.Unauthenticated)
            .Verifiable(Times.Once);

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        Assert.False(result.IsAuthenticated);
        Assert.False(result.IsUnavailable);
    }

    [Fact]
    public async Task ValidateAsync_ControlPlaneUnavailable_IsPassedThrough()
    {
        var httpContext = CreateHttpContext(ControlPlaneIssuer);
        var unavailable = ManagementTokenValidation.Unavailable(TimeSpan.FromSeconds(5));
        ControlPlaneMock
            .Setup(x => x.ValidateAsync(httpContext, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unavailable)
            .Verifiable(Times.Once);

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        Assert.Same(unavailable, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic abc")]
    [InlineData("DPoP not-a-jwt")]
    public async Task ValidateAsync_NoReadableToken_IsUnauthenticatedWithoutValidating(
        string? authorization
    )
    {
        var httpContext = new DefaultHttpContext();
        if (authorization is not null)
            httpContext.Request.Headers.Authorization = authorization;

        var result = await CreateValidator().ValidateAsync(httpContext, CancellationToken.None);

        Assert.False(result.IsAuthenticated);
    }
}
