// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory.Server.Options;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryServerOptionsValidatorTests
{
    private const string ControlPlaneCell = "control";

    private const string DisallowedIssuerMessage =
        "DirectoryServer:Issuer must be an absolute https URL (or http on a loopback host) of at most 1000 characters with no user information, query or fragment.";

    public static TheoryData<string> NonLabelControlPlaneCells =>
        ["Control-Plane", "1cell", "cell-", new string('a', CellLabelRules.MaxLabelLength + 1)];

    private static DirectoryServerOptionsValidator CreateValidator() => new();

    private static string CreateIssuerOfLength(int length)
    {
        const string prefix = "https://auth.example/";
        return prefix + new string('a', length - prefix.Length);
    }

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
    public void Validate_DisallowedIssuer_FailsNamingTheKeyAndRule(string issuer)
    {
        var options = new DirectoryServerOptions
        {
            Issuer = issuer,
            ControlPlaneCell = ControlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(DisallowedIssuerMessage, failure);
    }

    [Theory]
    [InlineData("/directory")]
    [InlineData("directory.example.com/path")]
    public void Validate_WhenTheIssuerHasNoSchemeOfItsOwn_Fails(string issuer)
    {
        var options = new DirectoryServerOptions
        {
            Issuer = issuer,
            ControlPlaneCell = ControlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(DisallowedIssuerMessage, failure);
    }

    [Fact]
    public void Validate_IssuerWithUserInformation_FailsWithoutEchoingIt()
    {
        var options = new DirectoryServerOptions
        {
            Issuer = "https://user:secret@auth.example",
            ControlPlaneCell = ControlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(DisallowedIssuerMessage, failure);
        Assert.DoesNotContain("secret", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_IssuerOverMaxLength_Fails()
    {
        var options = new DirectoryServerOptions
        {
            Issuer = CreateIssuerOfLength(DirectoryLimits.MaxIssuerLength + 1),
            ControlPlaneCell = ControlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(DisallowedIssuerMessage, failure);
    }

    [Fact]
    public void Validate_IssuerAtMaxLength_Succeeds()
    {
        var options = new DirectoryServerOptions
        {
            Issuer = CreateIssuerOfLength(DirectoryLimits.MaxIssuerLength),
            ControlPlaneCell = ControlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_SeveralFaults_ReportsEveryFailure()
    {
        var options = new DirectoryServerOptions
        {
            Issuer = "ftp://auth.example",
            ControlPlaneCell = null,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            [DisallowedIssuerMessage, "DirectoryServer:ControlPlaneCell is required."],
            result.Failures
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

    [Theory]
    [MemberData(nameof(NonLabelControlPlaneCells))]
    public void Validate_WhenControlPlaneCellIsNotACellLabel_FailsNamingTheKeyAndRule(
        string controlPlaneCell
    )
    {
        var options = new DirectoryServerOptions
        {
            Issuer = "https://auth.example",
            ControlPlaneCell = controlPlaneCell,
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"DirectoryServer:ControlPlaneCell must be a cell name: 1 to {CellLabelRules.MaxLabelLength} lowercase letters, digits or hyphens that start with a letter and end with a letter or digit.",
            failure
        );
    }

    #endregion
}
