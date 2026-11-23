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
        Assert.Contains("Mail:AmazonSes:Region", failure);
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
        Assert.Contains("Mail:AmazonSes:From", failure);
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
        Assert.Contains("Mail:AmazonSes:FromIdentityArn", failure);
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
        Assert.Contains("Mail:AmazonSes:ConfigurationSetName", failure);
    }

    [Fact]
    public void Validate_WithAConfigurationSetNameOver64Characters_Fails()
    {
        var options = CreateOptions();
        options.ConfigurationSetName = new string('a', 65);

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Mail:AmazonSes:ConfigurationSetName", failure);
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
        Assert.Contains("Mail:AmazonSes:Tags", failure);
        Assert.Contains("50", failure);
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
        Assert.Contains("Mail:AmazonSes:Tags", failure);
        Assert.Contains("name", failure);
    }

    [Fact]
    public void Validate_WithATagNameOver256Characters_Fails()
    {
        var options = CreateOptions();
        options.Tags[new string('a', 257)] = "value";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Mail:AmazonSes:Tags", failure);
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
        Assert.Contains("Mail:AmazonSes:Tags:team", failure);
    }

    [Fact]
    public void Validate_WithATagValueOver256Characters_Fails()
    {
        var options = CreateOptions();
        options.Tags["team"] = new string('a', 257);

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Mail:AmazonSes:Tags:team", failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(300_001)]
    public void Validate_WithATimeoutOutsideTheRange_Fails(int milliseconds)
    {
        var options = CreateOptions();
        options.Timeout = TimeSpan.FromMilliseconds(milliseconds);

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Mail:AmazonSes:Timeout", failure);
    }

    [Fact]
    public void Validate_WithSeveralRefusals_ReportsEachOne()
    {
        var options = CreateOptions();
        options.Region = string.Empty;
        options.From = string.Empty;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures.Count());
    }

    #endregion
}
