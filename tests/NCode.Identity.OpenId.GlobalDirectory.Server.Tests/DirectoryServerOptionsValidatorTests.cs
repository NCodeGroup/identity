// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory.Server.Options;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryServerOptionsValidatorTests
{
    private const string ControlPlaneCell = "control";

    private static DirectoryServerOptionsValidator CreateValidator() => new();

    #region Validate Tests

    [Theory]
    [InlineData("https://auth.example")]
    [InlineData("https://auth.example/tenants/root")]
    public void Validate_HttpsIssuer_Succeeds(string issuer)
    {
        var options = new DirectoryServerOptions
        {
            Issuer = issuer,
            ControlPlaneCell = ControlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("http://localhost:5000")]
    [InlineData("http://127.0.0.1:5000")]
    public void Validate_LoopbackHttpIssuer_Succeeds(string issuer)
    {
        var options = new DirectoryServerOptions
        {
            Issuer = issuer,
            ControlPlaneCell = ControlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyIssuer_FailsAsRequired(string? issuer)
    {
        var options = new DirectoryServerOptions
        {
            Issuer = issuer,
            ControlPlaneCell = ControlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("DirectoryServer:Issuer is required.", failure);
    }

    [Theory]
    [InlineData("auth.example")]
    [InlineData("/relative")]
    [InlineData("http://example.com")]
    [InlineData("https://auth.example/?x=1")]
    [InlineData("https://auth.example/#section")]
    [InlineData("ftp://auth.example")]
    public void Validate_DisallowedIssuer_FailsNamingTheValue(string issuer)
    {
        var options = new DirectoryServerOptions
        {
            Issuer = issuer,
            ControlPlaneCell = ControlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"DirectoryServer:Issuer '{issuer}' must be an absolute https URL, or an http URL on a loopback host, with no query or fragment.",
            failure
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyControlPlaneCell_FailsAsRequired(string? controlPlaneCell)
    {
        var options = new DirectoryServerOptions
        {
            Issuer = "https://auth.example",
            ControlPlaneCell = controlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("DirectoryServer:ControlPlaneCell is required.", failure);
    }

    #endregion
}
