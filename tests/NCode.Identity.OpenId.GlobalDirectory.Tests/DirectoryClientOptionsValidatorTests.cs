// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Time;
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
    [InlineData("https://directory.example/?")]
    [InlineData("https://@directory.example/")]
    public void Validate_WhenUrlIsNotAllowed_FailsNamingTheValue(string url)
    {
        var options = CreateOptions();
        options.Url = url;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:Url", failure);
        if (url.Length > 0)
            Assert.Contains(DirectoryUrls.Describe(url), failure);
    }

    [Fact]
    public void Validate_WhenUrlCarriesUserInformation_FailsWithoutEchoingTheCredentials()
    {
        var options = CreateOptions();
        options.Url = "https://user:secret@directory.example/path?q=x";

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:Url", failure);
        Assert.Contains("no user information", failure);
        Assert.Contains("https://directory.example/path", failure);
        Assert.DoesNotContain("secret", failure);
        Assert.DoesNotContain("q=x", failure);
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
    [InlineData("directory")]
    [InlineData("cell-")]
    [InlineData("cell-A")]
    [InlineData("other-a")]
    public void Validate_WhenClientIdIsNotACellClientId_FailsNamingTheValue(string clientId)
    {
        var options = CreateOptions();
        options.ClientId = clientId;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:ClientId", failure);
        Assert.Contains(clientId, failure);
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

    [Fact]
    public void Validate_WhenPollIntervalExceedsTheTimerMaximum_FailsNamingTheValue()
    {
        AssertPollIntervalFails(TimerPeriod.MaxValue + TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Validate_WhenPollIntervalIsTheTimerMinimum_Succeeds()
    {
        var options = CreateOptions();
        options.PollInterval = TimerPeriod.MinValue;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenPollIntervalIsTheTimerMaximum_Succeeds()
    {
        var options = CreateOptions();
        options.PollInterval = TimerPeriod.MaxValue;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenTheTotalTimeoutIsOutOfRange_FailsNamingTheKey()
    {
        var options = CreateOptions();
        options.TotalTimeout = TimeSpan.Zero;
        options.AttemptTimeout = TimeSpan.Zero;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Directory:TotalTimeout"));
    }

    [Fact]
    public void Validate_WhenTheAttemptTimeoutIsOutOfRange_FailsNamingTheKey()
    {
        var options = CreateOptions();
        options.AttemptTimeout = TimeSpan.FromHours(1);
        options.TotalTimeout = TimeSpan.FromHours(2);

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Directory:AttemptTimeout"));
    }

    [Fact]
    public void Validate_WhenTheAttemptTimeoutExceedsTheTotalTimeout_FailsNamingBothKeys()
    {
        var options = CreateOptions();
        options.AttemptTimeout = TimeSpan.FromSeconds(20);
        options.TotalTimeout = TimeSpan.FromSeconds(10);

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:AttemptTimeout", failure);
        Assert.Contains("Directory:TotalTimeout", failure);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void Validate_WhenMaxRetryAttemptsIsOutOfRange_FailsNamingTheKey(int attempts)
    {
        var options = CreateOptions();
        options.MaxRetryAttempts = attempts;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:MaxRetryAttempts", failure);
    }

    [Fact]
    public void Validate_WhenTheRetryDelayIsNegative_FailsNamingTheKey()
    {
        var options = CreateOptions();
        options.RetryDelay = TimeSpan.FromSeconds(-1);

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:RetryDelay", failure);
    }

    [Fact]
    public void Validate_WhenTheTokenFailureCacheDurationIsNotATimerPeriod_FailsNamingTheKey()
    {
        var options = CreateOptions();
        options.TokenFailureCacheDuration = TimeSpan.Zero;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Directory:TokenFailureCacheDuration", failure);
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
