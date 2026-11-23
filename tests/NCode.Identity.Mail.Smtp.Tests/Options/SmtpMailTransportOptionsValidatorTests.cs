// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore.Options;
using NCode.Identity.Mail.Smtp.Options;
using Xunit;

namespace NCode.Identity.Mail.Smtp.Tests.Options;

public class SmtpMailTransportOptionsValidatorTests : BaseTests
{
    private SmtpMailTransportOptionsValidator Validator { get; } = CreateValidator(false);

    private static SmtpMailTransportOptionsValidator CreateValidator(
        bool localDevelopmentEnabled
    ) =>
        new(
            new OptionsWrapper<LocalDevelopmentOptions>(new() { Enabled = localDevelopmentEnabled })
        );

    private static SmtpMailTransportOptions CreateOptions() =>
        new() { Host = "smtp.example.com", From = "noreply@example.com" };

    #region Validate Tests

    [Fact]
    public void Validate_WithAHostAndASender_Succeeds()
    {
        var result = Validator.Validate(null, CreateOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithEveryOptionSet_Succeeds()
    {
        var options = CreateOptions();
        options.Port = 465;
        options.Tls = SmtpTls.ImplicitTls;
        options.UserName = "smtp-user";
        options.Password = "smtp-password";
        options.FromName = "NCode Identity";
        options.Timeout = TimeSpan.FromMinutes(1);

        var result = Validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithAnEmptyHost_Fails()
    {
        var options = CreateOptions();
        options.Host = string.Empty;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Mail:Smtp:Host", failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_WithAPortOutsideTheRange_Fails(int port)
    {
        var options = CreateOptions();
        options.Port = port;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Mail:Smtp:Port", failure);
    }

    [Fact]
    public void Validate_WithAnUndefinedTls_Fails()
    {
        var options = CreateOptions();
        options.Tls = (SmtpTls)42;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Mail:Smtp:Tls", failure);
    }

    [Fact]
    public void Validate_WhenTlsIsNoneOutsideLocalDevelopment_Fails()
    {
        var options = CreateOptions();
        options.Tls = SmtpTls.None;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Mail:Smtp:Tls", failure);
        Assert.Contains("clear text", failure);
        Assert.Contains("LocalDevelopment:Enabled", failure);
    }

    [Fact]
    public void Validate_WhenTlsIsNoneInLocalDevelopment_Succeeds()
    {
        var options = CreateOptions();
        options.Tls = SmtpTls.None;
        var validator = CreateValidator(true);

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
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
        Assert.Contains("Mail:Smtp:From", failure);
    }

    [Fact]
    public void Validate_WithAUserNameWithoutAPassword_Fails()
    {
        var options = CreateOptions();
        options.UserName = "smtp-user";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Mail:Smtp:UserName", failure);
    }

    [Fact]
    public void Validate_WithAPasswordWithoutAUserName_Fails()
    {
        var options = CreateOptions();
        options.Password = "smtp-password";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Mail:Smtp:Password", failure);
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
        Assert.Contains("Mail:Smtp:Timeout", failure);
    }

    [Fact]
    public void Validate_WithSeveralRefusals_ReportsEachOne()
    {
        var options = CreateOptions();
        options.Host = string.Empty;
        options.Port = 0;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures.Count());
    }

    #endregion
}
