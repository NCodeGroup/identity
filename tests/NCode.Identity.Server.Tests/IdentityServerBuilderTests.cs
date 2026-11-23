// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Dispatch;
using NCode.Identity.Mail;
using Xunit;

namespace NCode.Identity.Server.Tests;

public sealed class IdentityServerBuilderTests : BaseTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenCreated_RegistersNoPropertyBagFactory()
    {
        var serviceCollection = new ServiceCollection();

        _ = new IdentityServerBuilder(serviceCollection);

        Assert.Contains(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(IJobDispatcher)
        );
        Assert.DoesNotContain(
            serviceCollection,
            descriptor => descriptor.ServiceType.FullName == "NCode.PropertyBag.IPropertyBagFactory"
        );
    }

    [Fact]
    public void Constructor_Always_RegistersTheMailSenderAndItsDefaultTransport()
    {
        var serviceCollection = new ServiceCollection();

        var builder = new IdentityServerBuilder(serviceCollection);

        Assert.Contains(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(IMailSender)
        );
        Assert.Contains(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(IMailTransport)
        );
        Assert.NotNull(builder.MailLibraryBuilder);
    }

    #endregion
}
