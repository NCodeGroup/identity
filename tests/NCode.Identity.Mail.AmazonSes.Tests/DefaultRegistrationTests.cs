// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Identity.Mail.AmazonSes.Facades;
using NCode.Identity.Mail.AmazonSes.Options;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.Mail.AmazonSes.Tests;

public class DefaultRegistrationTests : BaseTests
{
    private IMailTransport TransportRegisteredBefore { get; }

    public DefaultRegistrationTests()
    {
        TransportRegisteredBefore = CreateStrictMock<IMailTransport>().Object;
    }

    private static IConfiguration CreateConfiguration(
        params IEnumerable<(string Key, string? Value)> values
    ) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(x => KeyValuePair.Create(x.Key, x.Value)))
            .Build();

    private ServiceCollection AddAmazonSesMailTransport(IConfiguration configuration)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(TransportRegisteredBefore);
        serviceCollection.NewBuilder<MailLibrary>().AddAmazonSesMailTransport(configuration);
        return serviceCollection;
    }

    #region AddAmazonSesMailTransport Tests

    [Fact]
    public async Task AddAmazonSesMailTransport_WithoutTheSection_KeepsTheRegisteredTransport()
    {
        var serviceCollection = AddAmazonSesMailTransport(CreateConfiguration());

        await using var provider = serviceCollection.BuildServiceProvider();

        Assert.Same(TransportRegisteredBefore, provider.GetRequiredService<IMailTransport>());
        Assert.DoesNotContain(
            serviceCollection,
            x => x.ServiceType == typeof(IAmazonSesClientFactory)
        );
    }

    [Fact]
    public async Task AddAmazonSesMailTransport_WithTheSection_RegistersTheAmazonSesTransport()
    {
        var serviceCollection = AddAmazonSesMailTransport(
            CreateConfiguration(
                ("Mail:AmazonSes:Region", "us-east-1"),
                ("Mail:AmazonSes:From", "noreply@example.com"),
                ("Mail:AmazonSes:ConfigurationSetName", "ncode-identity"),
                ("Mail:AmazonSes:Tags:cell", "ci"),
                ("Mail:AmazonSes:Timeout", "00:00:10")
            )
        );

        await using var provider = serviceCollection.BuildServiceProvider();

        Assert.IsType<AmazonSesMailTransport>(
            Assert.Single(provider.GetServices<IMailTransport>())
        );
        Assert.IsType<DefaultAmazonSesClientFactory>(
            provider.GetRequiredService<IAmazonSesClientFactory>()
        );
        var options = provider.GetRequiredService<IOptions<AmazonSesMailTransportOptions>>().Value;
        Assert.Equal("us-east-1", options.Region);
        Assert.Equal("noreply@example.com", options.From);
        Assert.Equal("ncode-identity", options.ConfigurationSetName);
        Assert.Equal("ci", options.Tags["cell"]);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
    }

    [Fact]
    public void AddAmazonSesMailTransport_WhenAnotherTransportIsRegistered_Throws()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(TransportRegisteredBefore);
        serviceCollection.AddSingleton(new MailTransportRegistration("Mail:Smtp"));
        var configuration = CreateConfiguration(
            ("Mail:AmazonSes:Region", "us-east-1"),
            ("Mail:AmazonSes:From", "noreply@example.com")
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceCollection.NewBuilder<MailLibrary>().AddAmazonSesMailTransport(configuration)
        );

        Assert.Equal(
            "The mail transport 'Mail:Smtp' is registered; configure one of 'Mail:Smtp' and 'Mail:AmazonSes'.",
            exception.Message
        );
    }

    [Fact]
    public async Task AddAmazonSesMailTransport_WithTheSection_RegistersTheTransportMarker()
    {
        var serviceCollection = AddAmazonSesMailTransport(
            CreateConfiguration(
                ("Mail:AmazonSes:Region", "us-east-1"),
                ("Mail:AmazonSes:From", "noreply@example.com")
            )
        );

        await using var provider = serviceCollection.BuildServiceProvider();

        var registration = provider.GetRequiredService<MailTransportRegistration>();
        Assert.Equal("Mail:AmazonSes", registration.Name);
    }

    [Fact]
    public async Task AddAmazonSesMailTransport_WithoutTheSection_RegistersNoTransportMarker()
    {
        var serviceCollection = AddAmazonSesMailTransport(CreateConfiguration());

        await using var provider = serviceCollection.BuildServiceProvider();

        Assert.Empty(provider.GetServices<MailTransportRegistration>());
    }

    [Fact]
    public async Task AddAmazonSesMailTransport_WithTheSection_RegistersTheValidator()
    {
        var serviceCollection = AddAmazonSesMailTransport(
            CreateConfiguration(("Mail:AmazonSes:Region", "us-east-1"))
        );

        await using var provider = serviceCollection.BuildServiceProvider();

        Assert.IsType<AmazonSesMailTransportOptionsValidator>(
            Assert.Single(provider.GetServices<IValidateOptions<AmazonSesMailTransportOptions>>())
        );
    }

    [Fact]
    public async Task AddAmazonSesMailTransport_WhenTheSectionHasAnUnknownKey_ThrowsWhenTheOptionsAreRead()
    {
        var serviceCollection = AddAmazonSesMailTransport(
            CreateConfiguration(
                ("Mail:AmazonSes:Region", "us-east-1"),
                ("Mail:AmazonSes:From", "noreply@example.com"),
                ("Mail:AmazonSes:Regoin", "us-west-2")
            )
        );

        await using var provider = serviceCollection.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IOptions<AmazonSesMailTransportOptions>>().Value
        );
        Assert.Contains("Mail:AmazonSes", exception.Message);
    }

    [Fact]
    public async Task AddAmazonSesMailTransport_WhenTheRegionIsNotARegion_FailsAtStart()
    {
        var serviceCollection = AddAmazonSesMailTransport(
            CreateConfiguration(
                ("Mail:AmazonSes:Region", "US-EAST"),
                ("Mail:AmazonSes:From", "noreply@example.com")
            )
        );

        await using var provider = serviceCollection.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AmazonSesMailTransportOptions>>().Value
        );
        Assert.Contains("Mail:AmazonSes:Region", Assert.Single(exception.Failures));
    }

    #endregion
}
