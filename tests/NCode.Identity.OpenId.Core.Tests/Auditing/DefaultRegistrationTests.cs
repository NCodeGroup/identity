// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NCode.Collections.Providers;
using NCode.Identity.OpenId.Authentication;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Authentication.Auditing.Streams;
using NCode.Identity.Registration;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Auditing;

public sealed class DefaultRegistrationTests
{
    #region AddAuditingServices Tests

    [Fact]
    public void AddAuditingServices_Always_RegistersTheStreamDelivery()
    {
        var serviceCollection = new ServiceCollection();

        new ServiceBuilder<OpenIdAuthenticationLibrary>(serviceCollection).AddAuditingServices();

        var deliverer = Assert.Single(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(IAuditStreamDeliverer)
        );
        Assert.Equal(typeof(DefaultAuditStreamDeliverer), deliverer.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, deliverer.Lifetime);

        foreach (
            var hostedType in new[]
            {
                typeof(AuditStreamDeliveryService),
                typeof(AuditRecordFlushService),
                typeof(AuditRecordPruneService),
            }
        )
        {
            var hosted = Assert.Single(
                serviceCollection,
                descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == hostedType
            );
            Assert.Equal(ServiceLifetime.Singleton, hosted.Lifetime);
        }

        var setting = Assert.Single(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(ICollectionDataSource<SettingDescriptor>)
        );
        Assert.Equal(
            typeof(DefaultAuditStreamSettingDescriptorDataSource),
            setting.ImplementationType
        );
        Assert.Equal(ServiceLifetime.Singleton, setting.Lifetime);
    }

    #endregion
}
