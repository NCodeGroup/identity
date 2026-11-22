// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NCode.Identity.Server.Tests;

public sealed class IdentityServerBuilderTests : BaseTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_RegistersNoPropertyBagFactory()
    {
        var services = new ServiceCollection();

        _ = new IdentityServerBuilder(services);

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.FullName == "NCode.PropertyBag.IPropertyBagFactory"
        );
    }

    #endregion
}
