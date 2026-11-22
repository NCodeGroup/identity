// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Dispatch;
using Xunit;

namespace NCode.Identity.Mail.Tests;

public class DefaultRegistrationTests : BaseTests
{
    #region AddMailServices Tests

    [Fact]
    public void AddMailServices_Always_RegistersTheSenderTheTransportAndTheJobHandler()
    {
        var serviceCollection = new ServiceCollection();

        var builder = serviceCollection.AddMailServices();

        Assert.NotNull(builder);
        Assert.Single(
            serviceCollection,
            descriptor =>
                descriptor.ServiceType == typeof(IMailSender)
                && descriptor.ImplementationType == typeof(DefaultMailSender)
                && descriptor.Lifetime == ServiceLifetime.Singleton
        );
        Assert.Single(
            serviceCollection,
            descriptor =>
                descriptor.ServiceType == typeof(IMailTransport)
                && descriptor.ImplementationType == typeof(UnconfiguredMailTransport)
                && descriptor.Lifetime == ServiceLifetime.Singleton
        );
        Assert.Single(
            serviceCollection,
            descriptor =>
                descriptor.ServiceType == typeof(IDispatchJobHandler<MailDeliveryJob>)
                && descriptor.ImplementationType == typeof(MailDeliveryJobHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped
        );
        Assert.Single(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(MailDeliveryMetrics)
        );
    }

    [Fact]
    public void AddMailServices_WhenATransportIsRegisteredFirst_KeepsIt()
    {
        var transport = CreateStrictMock<IMailTransport>().Object;
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(transport);

        serviceCollection.AddMailServices();

        using var serviceProvider = serviceCollection.BuildServiceProvider();
        Assert.Same(transport, serviceProvider.GetRequiredService<IMailTransport>());
    }

    #endregion
}
