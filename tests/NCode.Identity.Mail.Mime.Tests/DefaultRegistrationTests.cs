// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.Mail.Mime.Tests;

public class DefaultRegistrationTests : BaseTests
{
    private IMailTransport TransportRegisteredBefore { get; }

    public DefaultRegistrationTests()
    {
        TransportRegisteredBefore = CreateStrictMock<IMailTransport>().Object;
    }

    #region AddMimeMessageFactory Tests

    [Fact]
    public async Task AddMimeMessageFactory_Always_RegistersTheDefaultFactoryAndTheSystemClock()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.NewBuilder<MailLibrary>().AddMimeMessageFactory();

        await using var provider = serviceCollection.BuildServiceProvider();

        Assert.IsType<DefaultMimeMessageFactory>(
            provider.GetRequiredService<IMimeMessageFactory>()
        );
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task AddMimeMessageFactory_WithATimeProviderRegisteredBefore_KeepsIt()
    {
        var clock = new FakeTimeProvider();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<TimeProvider>(clock);

        serviceCollection.NewBuilder<MailLibrary>().AddMimeMessageFactory();

        await using var provider = serviceCollection.BuildServiceProvider();

        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
    }

    #endregion

    #region AddMimeMailTransport Tests

    [Fact]
    public async Task AddMimeMailTransport_Always_RegistersTheTransportWithTheFactoryInjected()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(TransportRegisteredBefore);

        serviceCollection
            .NewBuilder<MailLibrary>()
            .AddMimeMailTransport<FakeMailTransport>("Mail:Test");

        await using var provider = serviceCollection.BuildServiceProvider();

        var transport = Assert.IsType<FakeMailTransport>(
            Assert.Single(provider.GetServices<IMailTransport>())
        );
        Assert.Same(
            provider.GetRequiredService<IMimeMessageFactory>(),
            transport.MimeMessageFactory
        );
        Assert.Equal("Mail:Test", provider.GetRequiredService<MailTransportRegistration>().Name);
    }

    [Fact]
    public void AddMimeMailTransport_WhenAnotherTransportIsRegistered_Throws()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(new MailTransportRegistration("Mail:Smtp"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            serviceCollection
                .NewBuilder<MailLibrary>()
                .AddMimeMailTransport<FakeMailTransport>("Mail:AmazonSes")
        );

        Assert.Equal(
            "The mail transport 'Mail:Smtp' is registered, so the mail transport 'Mail:AmazonSes' cannot be; a host configures one mail transport.",
            exception.Message
        );
    }

    [Fact]
    public void AddMimeMailTransport_WithAnEmptyName_Throws()
    {
        var serviceCollection = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            serviceCollection.NewBuilder<MailLibrary>().AddMimeMailTransport<FakeMailTransport>("")
        );
    }

    #endregion
}
