// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Mail.AmazonSes.Options;
using Xunit;

namespace NCode.Identity.Mail.AmazonSes.Tests.Options;

public class AmazonSesMailTransportOptionsValidatorTests : BaseTests
{
    private AmazonSesMailTransportOptionsValidator Validator { get; } = new();

    private static AmazonSesMailTransportOptions CreateOptions() =>
        new() { Region = "us-east-1", From = "noreply@example.com" };

    #region Validate Tests

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var options = CreateOptions();
        options.FromName = "NCode Identity";
        options.FromIdentityArn = "arn:aws:ses:us-east-1:123456789012:identity/example.com";
        options.ConfigurationSetName = "ncode_identity-mail";
        options.Tags["team"] = "platform";
        options.Tags["cell.name"] = "cell-1@example";
        options.Timeout = TimeSpan.FromMinutes(1);

        var result = Validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithOnlyARegionAndASender_Succeeds()
    {
        var result = Validator.Validate(null, CreateOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("us-east")]
    [InlineData("US-EAST-1")]
    [InlineData("useast1")]
    [InlineData("us-east-1\n")]
    [InlineData("us-east-١")]
    public void Validate_WithARegionThatIsNotARegion_Fails(string region)
    {
        var options = CreateOptions();
        options.Region = region;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"AmazonSesMailTransportOptions.Region ('Mail:AmazonSes:Region') must name an AWS region such as 'us-east-1'; it is '{region}'.",
            failure
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("local-part-only")]
    [InlineData("a@example.com, b@example.com")]
    public void Validate_WithASenderThatIsNotOneAddress_Fails(string from)
    {
        var options = CreateOptions();
        options.From = from;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"AmazonSesMailTransportOptions.From ('Mail:AmazonSes:From') must be one mail address; it is '{from}'.",
            failure
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("arn:aws:ses:us-east-1:123456789012:configuration-set/example")]
    [InlineData("arn:aws:ses:us-east-1:12345:identity/example.com")]
    [InlineData("arn:aws:ses:us-east-1:123456789012:identity/")]
    public void Validate_WithAnIdentityArnThatIsNotAnSesIdentity_Fails(string arn)
    {
        var options = CreateOptions();
        options.FromIdentityArn = arn;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"AmazonSesMailTransportOptions.FromIdentityArn ('Mail:AmazonSes:FromIdentityArn') must be the ARN of an Amazon SES identity; it is '{arn}'.",
            failure
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has.dot")]
    public void Validate_WithAConfigurationSetNameOutsideThePattern_Fails(string configurationSet)
    {
        var options = CreateOptions();
        options.ConfigurationSetName = configurationSet;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"AmazonSesMailTransportOptions.ConfigurationSetName ('Mail:AmazonSes:ConfigurationSetName') must be 1 to 64 letters, digits, underscores or hyphens; it is '{configurationSet}'.",
            failure
        );
    }

    [Fact]
    public void Validate_WithAConfigurationSetNameOver64Characters_Fails()
    {
        var configurationSet = new string('a', 65);
        var options = CreateOptions();
        options.ConfigurationSetName = configurationSet;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"AmazonSesMailTransportOptions.ConfigurationSetName ('Mail:AmazonSes:ConfigurationSetName') must be 1 to 64 letters, digits, underscores or hyphens; it is '{configurationSet}'.",
            failure
        );
    }

    [Fact]
    public void Validate_WithMoreThan50Tags_Fails()
    {
        var options = CreateOptions();
        for (var index = 0; index < 51; index++)
        {
            options.Tags[$"tag{index}"] = "value";
        }

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            "AmazonSesMailTransportOptions.Tags ('Mail:AmazonSes:Tags') must hold at most 50 tags; it holds 51.",
            failure
        );
    }

    [Fact]
    public void Validate_With50Tags_Succeeds()
    {
        var options = CreateOptions();
        for (var index = 0; index < 50; index++)
        {
            options.Tags[$"tag{index}"] = "value";
        }

        var result = Validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has:colon")]
    public void Validate_WithATagNameOutsideThePattern_Fails(string tagName)
    {
        var options = CreateOptions();
        options.Tags[tagName] = "value";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"AmazonSesMailTransportOptions.Tags ('Mail:AmazonSes:Tags') must name each tag with 1 to 256 letters, digits, underscores, periods, at signs or hyphens; the name '{tagName}' does not.",
            failure
        );
    }

    [Fact]
    public void Validate_WithATagNameOver256Characters_Fails()
    {
        var tagName = new string('a', 257);
        var options = CreateOptions();
        options.Tags[tagName] = "value";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"AmazonSesMailTransportOptions.Tags ('Mail:AmazonSes:Tags') must name each tag with 1 to 256 letters, digits, underscores, periods, at signs or hyphens; the name '{tagName}' does not.",
            failure
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    public void Validate_WithATagValueOutsideThePattern_Fails(string tagValue)
    {
        var options = CreateOptions();
        options.Tags["team"] = tagValue;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"AmazonSesMailTransportOptions.Tags ('Mail:AmazonSes:Tags:team') must be 1 to 256 letters, digits, underscores, periods, at signs or hyphens; it is '{tagValue}'.",
            failure
        );
    }

    [Fact]
    public void Validate_WithATagValueOver256Characters_Fails()
    {
        var tagValue = new string('a', 257);
        var options = CreateOptions();
        options.Tags["team"] = tagValue;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"AmazonSesMailTransportOptions.Tags ('Mail:AmazonSes:Tags:team') must be 1 to 256 letters, digits, underscores, periods, at signs or hyphens; it is '{tagValue}'.",
            failure
        );
    }

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(999, "00:00:00.9990000")]
    [InlineData(300_001, "00:05:00.0010000")]
    public void Validate_WithATimeoutOutsideTheRange_Fails(int milliseconds, string timeout)
    {
        var options = CreateOptions();
        options.Timeout = TimeSpan.FromMilliseconds(milliseconds);

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            $"AmazonSesMailTransportOptions.Timeout ('Mail:AmazonSes:Timeout') must be between 00:00:01 and 00:05:00; it is {timeout}.",
            failure
        );
    }

    [Fact]
    public void Validate_WithSeveralRefusals_ReportsEachOne()
    {
        var options = CreateOptions();
        options.Region = string.Empty;
        options.From = string.Empty;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var regionMessage =
            "AmazonSesMailTransportOptions.Region ('Mail:AmazonSes:Region') must name an AWS region such as 'us-east-1'; it is ''.";
        var fromMessage =
            "AmazonSesMailTransportOptions.From ('Mail:AmazonSes:From') must be one mail address; it is ''.";
        Assert.Equal([regionMessage, fromMessage], result.Failures);
    }

    #endregion
}
