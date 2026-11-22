// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Environments;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Environments;

public sealed class DefaultRegistrationTests : BaseTests
{
    #region AddEnvironmentServices Tests

    [Fact]
    public void AddEnvironmentServices_Always_RegistersTheHostDataProtection()
    {
        var serviceCollection = new ServiceCollection();

        new ServiceBuilder<OpenIdCoreLibrary>(serviceCollection).AddEnvironmentServices();

        Assert.Contains(serviceCollection, x => x.ServiceType == typeof(IDataProtectionProvider));
    }

    #endregion
}
