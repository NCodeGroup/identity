// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class LocalPlatformTopologyTests : BaseTests
{
    private static LocalPlatformTopology CreateTopology(bool isControlPlane) =>
        new(Options.Create(new CellOptions { IsControlPlane = isControlPlane }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HasManagementTenant_WhenTheCellServesADynamicStrategy_IsFalse(bool isControlPlane)
    {
        var topology = CreateTopology(isControlPlane);

        Assert.False(topology.HasManagementTenant);
    }

    [Fact]
    public void HasDirectory_IsFalse()
    {
        var topology = CreateTopology(isControlPlane: false);

        Assert.False(topology.HasDirectory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsControlPlaneCell_Always_ReadsTheCellOptions(bool isControlPlane)
    {
        var topology = CreateTopology(isControlPlane);

        Assert.Equal(isControlPlane, topology.IsControlPlaneCell);
    }
}
