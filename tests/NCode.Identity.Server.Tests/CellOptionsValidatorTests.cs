// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.Server.Cells;
using Xunit;

namespace NCode.Identity.Server.Tests;

public sealed class CellOptionsValidatorTests
{
    private static CellOptionsValidator CreateValidator(bool localDevelopment = false) =>
        new(new LocalDevelopmentMode(localDevelopment));

    private static CellOptions CreateOptions() =>
        new()
        {
            Name = "eu-1",
            Region = "eu",
            Kind = CellKinds.Shared,
            IsControlPlane = true,
        };

    #region Validate Tests

    [Fact]
    public void Validate_WhenTheCellIsTheControlPlane_Succeeds()
    {
        var result = CreateValidator().Validate(name: null, CreateOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenTheCellIsNotTheControlPlaneAndNamesItsIssuer_Succeeds()
    {
        var options = CreateOptions();
        options.IsControlPlane = false;
        options.ControlPlane.Issuer = "https://auth.example";

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("EU")]
    [InlineData("1eu")]
    [InlineData("eu_1")]
    [InlineData("eu 1")]
    [InlineData("-eu")]
    [InlineData("eu-")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void Validate_WhenTheNameIsNotALabel_Fails(string value)
    {
        var options = CreateOptions();
        options.Name = value;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains("Cell:Name", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US-East")]
    [InlineData("2eu")]
    [InlineData("eu.west")]
    public void Validate_WhenTheRegionIsNotALabel_Fails(string value)
    {
        var options = CreateOptions();
        options.Region = value;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains("Cell:Region", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("eu-west-1")]
    [InlineData("abcdefghijklmnopqrstuvwxyz01234")]
    public void Validate_WhenTheNameAndRegionAreLabels_Succeeds(string value)
    {
        var options = CreateOptions();
        options.Name = value;
        options.Region = value;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_WhenTheKindIsEmpty_Fails(string kind)
    {
        var options = CreateOptions();
        options.Kind = kind;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains("Cell:Kind", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenTheKindIsNotAKnownKind_Succeeds()
    {
        // Cell kinds are an open vocabulary.
        var options = CreateOptions();
        options.Kind = "edge";

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auth.example")]
    [InlineData("/relative")]
    [InlineData("file:///relative")]
    [InlineData("ftp://auth.example")]
    [InlineData("http://auth.example")]
    [InlineData("https://auth.example/?x=1")]
    public void Validate_WhenTheCellIsNotTheControlPlaneAndTheIssuerIsNotAnAllowedEndpoint_Fails(
        string? issuer
    )
    {
        var options = CreateOptions();
        options.IsControlPlane = false;
        options.ControlPlane.Issuer = issuer;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "Cell:ControlPlane:Issuer",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("https://auth.example")]
    [InlineData("http://localhost:5000")]
    public void Validate_WhenTheCellIsNotTheControlPlaneAndTheIssuerIsAnAllowedEndpoint_Succeeds(
        string issuer
    )
    {
        var options = CreateOptions();
        options.IsControlPlane = false;
        options.ControlPlane.Issuer = issuer;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenTheIssuerCarriesUserInformation_Fails()
    {
        const string issuer = "https://user@auth.example";
        var options = CreateOptions();
        options.IsControlPlane = false;
        options.ControlPlane.Issuer = issuer;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            "Cell:ControlPlane:Issuer must be an absolute https URL, or an http URL on a loopback host, with no user information, query or fragment.",
            result.FailureMessage
        );
    }

    [Fact]
    public void Validate_WhenTheCellIsTheControlPlane_IgnoresTheIssuer()
    {
        var options = CreateOptions();
        options.ControlPlane.Issuer = null;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenTheManagementUrlIsUnset_Succeeds()
    {
        var options = CreateOptions();
        options.ManagementUrl = string.Empty;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenTheManagementUrlIsHttps_Succeeds()
    {
        var options = CreateOptions();
        options.ManagementUrl = "https://eu-1.example/";

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("eu-1.example")]
    [InlineData("/management")]
    [InlineData("ftp://eu-1.example/")]
    [InlineData("http://eu-1.example/")]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://127.0.0.1:5000/")]
    public void Validate_WhenTheManagementUrlIsNotAbsoluteHttpsOutsideLocalDevelopment_Fails(
        string managementUrl
    )
    {
        var options = CreateOptions();
        options.ManagementUrl = managementUrl;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            "Cell:ManagementUrl must be an absolute https URL with no user information, query or fragment.",
            result.FailureMessage
        );
    }

    [Theory]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://127.0.0.1:5000/")]
    [InlineData("http://[::1]:5000/")]
    public void Validate_WhenTheManagementUrlIsHttpOnALoopbackHostOutsideLocalDevelopment_Fails(
        string managementUrl
    )
    {
        var options = CreateOptions();
        options.ManagementUrl = managementUrl;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            "Cell:ManagementUrl must be an absolute https URL with no user information, query or fragment.",
            result.FailureMessage
        );
    }

    [Fact]
    public void Validate_WhenTheManagementUrlIsHttpOnAnyHostInLocalDevelopment_Succeeds()
    {
        var options = CreateOptions();
        options.ManagementUrl = "http://eu-1.example/";

        var result = CreateValidator(localDevelopment: true).Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenTheManagementUrlIsNotHttpOrHttpsInLocalDevelopment_Fails()
    {
        const string managementUrl = "ftp://localhost/";
        var options = CreateOptions();
        options.ManagementUrl = managementUrl;

        var result = CreateValidator(localDevelopment: true).Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            "Cell:ManagementUrl must be an absolute http or https URL with no user information, query or fragment.",
            result.FailureMessage
        );
    }

    [Theory]
    [InlineData("https://eu-1.example/?a=b")]
    [InlineData("https://eu-1.example/?")]
    public void Validate_WhenTheManagementUrlHasAQuery_Fails(string managementUrl)
    {
        var options = CreateOptions();
        options.ManagementUrl = managementUrl;

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            "Cell:ManagementUrl must be an absolute https URL with no user information, query or fragment.",
            result.FailureMessage
        );
    }

    [Fact]
    public void Validate_WhenTheIssuerCarriesAPassword_DoesNotEchoIt()
    {
        var options = CreateOptions();
        options.IsControlPlane = false;
        options.ControlPlane.Issuer = "https://user:secret@auth.example/";

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.All(
            result.Failures ?? [],
            failure => Assert.DoesNotContain("secret", failure, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Validate_WhenTheManagementUrlCarriesAPassword_DoesNotEchoIt()
    {
        var options = CreateOptions();
        options.ManagementUrl = "https://user:secret@host.example/";

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.All(
            result.Failures ?? [],
            failure => Assert.DoesNotContain("secret", failure, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Validate_WhenSeveralRulesFail_ReportsEveryFailure()
    {
        var options = new CellOptions
        {
            Name = "",
            Region = "",
            Kind = "",
            ManagementUrl = "http://eu-1.example/",
        };

        var result = CreateValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(5, result.Failures?.Count());
    }

    #endregion
}
