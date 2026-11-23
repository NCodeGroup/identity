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
        Assert.Equal(
            "SmtpMailTransportOptions.Host ('Mail:Smtp:Host') must name the SMTP server; it is empty.",
            failure
        );
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
        Assert.Equal(
            $"SmtpMailTransportOptions.Port ('Mail:Smtp:Port') must be between 1 and 65535; it is {port}.",
            failure
        );
    }

    [Fact]
    public void Validate_WithAnUndefinedTls_Fails()
    {
        var options = CreateOptions();
        options.Tls = (SmtpTls)42;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            "SmtpMailTransportOptions.Tls ('Mail:Smtp:Tls') must be one of StartTls, ImplicitTls, None; it is 42.",
            failure
        );
    }

    [Fact]
    public void Validate_WhenTlsIsNoneOutsideLocalDevelopment_Fails()
    {
        var options = CreateOptions();
        options.Tls = SmtpTls.None;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            "SmtpMailTransportOptions.Tls ('Mail:Smtp:Tls') is None, which sends mail and the codes it carries in clear text; only a host with LocalDevelopment:Enabled may use it.",
            failure
        );
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
        Assert.Equal(
            $"SmtpMailTransportOptions.From ('Mail:Smtp:From') must be one mail address; it is '{from}'.",
            failure
        );
    }

    [Fact]
    public void Validate_WithAUserNameWithoutAPassword_Fails()
    {
        var options = CreateOptions();
        options.UserName = "smtp-user";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            "SmtpMailTransportOptions.UserName and Password ('Mail:Smtp:UserName' and 'Mail:Smtp:Password') must be set together or not at all.",
            failure
        );
    }

    [Fact]
    public void Validate_WithAPasswordWithoutAUserName_Fails()
    {
        var options = CreateOptions();
        options.Password = "smtp-password";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            "SmtpMailTransportOptions.UserName and Password ('Mail:Smtp:UserName' and 'Mail:Smtp:Password') must be set together or not at all.",
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
            $"SmtpMailTransportOptions.Timeout ('Mail:Smtp:Timeout') must be between 00:00:01 and 00:05:00; it is {timeout}.",
            failure
        );
    }

    [Fact]
    public void Validate_WithSeveralRefusals_ReportsEachOne()
    {
        var options = CreateOptions();
        options.Host = string.Empty;
        options.Port = 0;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        var hostMessage =
            "SmtpMailTransportOptions.Host ('Mail:Smtp:Host') must name the SMTP server; it is empty.";
        var portMessage =
            "SmtpMailTransportOptions.Port ('Mail:Smtp:Port') must be between 1 and 65535; it is 0.";
        Assert.Equal([hostMessage, portMessage], result.Failures);
    }

    #endregion
}
