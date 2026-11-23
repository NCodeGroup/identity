// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NCode.Identity.Registration.Tests;

public sealed class SharedServiceBuilderTests
{
    private readonly struct SharedMarker : IRegistrationMarker<SharedMarker>;

    #region GetOrAddBuilder Tests

    [Fact]
    public void GetOrAddBuilder_WhenFirstCall_RegistersTheMarkerOnce()
    {
        var serviceCollection = new ServiceCollection();

        var builder = serviceCollection.GetOrAddBuilder<SharedMarker>();

        Assert.Same(serviceCollection, builder.ServiceCollection);
        Assert.Single(serviceCollection);
        Assert.True(serviceCollection.IsRegistered<SharedMarker>());
    }

    [Fact]
    public void GetOrAddBuilder_WhenSecondCall_DoesNotThrowAndKeepsOneMarker()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.GetOrAddBuilder<SharedMarker>();

        var builder = serviceCollection.GetOrAddBuilder<SharedMarker>();

        Assert.Same(serviceCollection, builder.ServiceCollection);
        Assert.Single(serviceCollection);
    }

    [Fact]
    public void NewBuilder_WhenAfterGetOrAddBuilder_Throws()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.GetOrAddBuilder<SharedMarker>();

        Assert.Throws<InvalidOperationException>(() =>
            serviceCollection.NewBuilder<SharedMarker>()
        );
    }

    #endregion
}
