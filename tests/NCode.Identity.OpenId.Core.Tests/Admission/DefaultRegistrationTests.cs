// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Collections.Providers;
using NCode.Identity.OpenId.Authentication;
using NCode.Identity.OpenId.Authentication.Admission;
using NCode.Identity.OpenId.Authentication.Metrics;
using NCode.Identity.Registration;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Admission;

public sealed class DefaultRegistrationTests
{
    #region AddAdmissionServices Tests

    [Fact]
    public void AddAdmissionServices_Always_RegistersTheGateItsOptionsItsSettingAndTheMetrics()
    {
        var serviceCollection = new ServiceCollection();

        new ServiceBuilder<OpenIdAuthenticationLibrary>(serviceCollection).AddAdmissionServices();

        var gate = Assert.Single(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(IRequestAdmissionGate)
        );
        Assert.Equal(typeof(DefaultRequestAdmissionGate), gate.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, gate.Lifetime);

        var validator = Assert.Single(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(IValidateOptions<AdmissionOptions>)
        );
        Assert.Equal(typeof(AdmissionOptionsValidator), validator.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, validator.Lifetime);

        var setting = Assert.Single(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(ICollectionDataSource<SettingDescriptor>)
        );
        Assert.Equal(
            typeof(DefaultAdmissionSettingDescriptorDataSource),
            setting.ImplementationType
        );
        Assert.Equal(ServiceLifetime.Singleton, setting.Lifetime);

        var metrics = Assert.Single(
            serviceCollection,
            descriptor => descriptor.ServiceType == typeof(AuthenticationMetrics)
        );
        Assert.Equal(typeof(AuthenticationMetrics), metrics.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, metrics.Lifetime);
    }

    #endregion
}
