// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Identity.Mail.Smtp.Facades;
using NCode.Identity.Mail.Smtp.Options;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.Mail.Smtp.Tests;

public class DefaultRegistrationTests : BaseTests
{
    private IMailTransport TransportRegisteredBefore { get; }

    private IHostEnvironment HostEnvironment { get; }

    public DefaultRegistrationTests()
    {
        TransportRegisteredBefore = CreateStrictMock<IMailTransport>().Object;
        HostEnvironment = CreateStrictMock<IHostEnvironment>().Object;
    }

    private static IConfiguration CreateConfiguration(
        params IEnumerable<(string Key, string? Value)> values
    ) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(x => KeyValuePair.Create(x.Key, x.Value)))
            .Build();

    private ServiceCollection AddSmtpMailTransport(IConfiguration configuration)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(TransportRegisteredBefore);
        serviceCollection.AddSingleton(HostEnvironment);
        serviceCollection.NewBuilder<MailLibrary>().AddSmtpMailTransport(configuration);
        return serviceCollection;
    }

    #region AddSmtpMailTransport Tests

    [Fact]
    public async Task AddSmtpMailTransport_WithoutTheSection_KeepsTheRegisteredTransport()
    {
        var serviceCollection = AddSmtpMailTransport(CreateConfiguration());

        await using var provider = serviceCollection.BuildServiceProvider();

        Assert.Same(TransportRegisteredBefore, provider.GetRequiredService<IMailTransport>());
        Assert.DoesNotContain(serviceCollection, x => x.ServiceType == typeof(ISmtpClientFactory));
    }

    [Fact]
    public async Task AddSmtpMailTransport_WithTheSection_RegistersTheSmtpTransport()
    {
        var serviceCollection = AddSmtpMailTransport(
            CreateConfiguration(
                ("Mail:Smtp:Host", "smtp.example.com"),
                ("Mail:Smtp:From", "noreply@example.com"),
                ("Mail:Smtp:Tls", "ImplicitTls"),
                ("Mail:Smtp:Port", "465"),
                ("Mail:Smtp:Timeout", "00:00:10")
            )
        );

        await using var provider = serviceCollection.BuildServiceProvider();

        Assert.IsType<SmtpMailTransport>(Assert.Single(provider.GetServices<IMailTransport>()));
        Assert.IsType<DefaultSmtpClientFactory>(provider.GetRequiredService<ISmtpClientFactory>());
        var options = provider.GetRequiredService<IOptions<SmtpMailTransportOptions>>().Value;
        Assert.Equal("smtp.example.com", options.Host);
        Assert.Equal("noreply@example.com", options.From);
        Assert.Equal(SmtpTls.ImplicitTls, options.Tls);
        Assert.Equal(465, options.Port);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
    }

    [Fact]
    public async Task AddSmtpMailTransport_WithTheSection_RegistersTheValidator()
    {
        var serviceCollection = AddSmtpMailTransport(
            CreateConfiguration(("Mail:Smtp:Host", "smtp.example.com"))
        );

        await using var provider = serviceCollection.BuildServiceProvider();

        Assert.IsType<SmtpMailTransportOptionsValidator>(
            Assert.Single(provider.GetServices<IValidateOptions<SmtpMailTransportOptions>>())
        );
    }

    [Fact]
    public async Task AddSmtpMailTransport_WhenTheSectionHasAnUnknownKey_ThrowsWhenTheOptionsAreRead()
    {
        var serviceCollection = AddSmtpMailTransport(
            CreateConfiguration(
                ("Mail:Smtp:Host", "smtp.example.com"),
                ("Mail:Smtp:From", "noreply@example.com"),
                ("Mail:Smtp:Hots", "smtp.example.net")
            )
        );

        await using var provider = serviceCollection.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IOptions<SmtpMailTransportOptions>>().Value
        );
        Assert.Contains("Mail:Smtp", exception.Message);
    }

    [Theory]
    [InlineData("2")]
    [InlineData(" None ")]
    [InlineData("StartTls, None")]
    public async Task AddSmtpMailTransport_WhenTlsIsAnotherSpellingOfNone_FailsAtStart(string tls)
    {
        var serviceCollection = AddSmtpMailTransport(
            CreateConfiguration(
                ("Mail:Smtp:Host", "smtp.example.com"),
                ("Mail:Smtp:From", "noreply@example.com"),
                ("Mail:Smtp:Tls", tls)
            )
        );

        await using var provider = serviceCollection.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<SmtpMailTransportOptions>>().Value
        );
        Assert.Contains("Mail:Smtp:Tls", Assert.Single(exception.Failures));
    }

    #endregion
}
