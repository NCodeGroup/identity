// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Management;

public sealed class DefaultRegistrationTests
{
    #region AddOpenIdManagementLibrary Tests

    [Fact]
    public void AddOpenIdManagementLibrary_Always_RegistersTheHostDataProtection()
    {
        var serviceCollection = new ServiceCollection();

        serviceCollection.NewBuilder<OpenIdCoreLibrary>().AddOpenIdManagementLibrary();

        Assert.Contains(serviceCollection, x => x.ServiceType == typeof(IDataProtectionProvider));
    }

    #endregion
}
