// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryClientOptionsValidatorTests : BaseTests
{
    private static DirectoryClientOptionsValidator CreateValidator() => new();

    private static DirectoryClientOptions CreateOptions() =>
        new()
        {
            Url = "https://directory.example",
            ClientId = "cell-a",
            KeyRing = "directory",
            PollInterval = TimeSpan.FromSeconds(30),
        };

    #region Validate Tests

    [Fact]
    public void Validate_WhenOptionsAreValid_Succeeds()
    {
        var result = CreateValidator().Validate(null, CreateOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("https://directory.example")]
    [InlineData("http://127.0.0.1:5000")]
    public void Validate_WhenUrlIsAllowed_Succeeds(string url)
    {
        var options = CreateOptions();
        options.Url = url;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("directory")]
    [InlineData("http://directory.example")]
    [InlineData("https://directory.example/?q=1")]
    public void Validate_WhenUrlIsNotAllowed_FailsNamingTheValue(string url)
    {
        var options = CreateOptions();
        options.Url = url;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:Url", failure);
        if (url.Length > 0)
            Assert.Contains(url, failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenClientIdIsMissing_FailsNamingTheKey(string? clientId)
    {
        var options = CreateOptions();
        options.ClientId = clientId;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:ClientId", failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenKeyRingIsMissing_FailsNamingTheKey(string? keyRing)
    {
        var options = CreateOptions();
        options.KeyRing = keyRing;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:KeyRing", failure);
    }

    [Fact]
    public void Validate_WhenPollIntervalIsZero_FailsNamingTheValue()
    {
        AssertPollIntervalFails(TimeSpan.Zero);
    }

    [Fact]
    public void Validate_WhenPollIntervalIsNegative_FailsNamingTheValue()
    {
        AssertPollIntervalFails(TimeSpan.FromSeconds(-5));
    }

    #endregion

    private static void AssertPollIntervalFails(TimeSpan pollInterval)
    {
        var options = CreateOptions();
        options.PollInterval = pollInterval;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:PollInterval", failure);
        Assert.Contains(pollInterval.ToString(), failure);
    }
}
