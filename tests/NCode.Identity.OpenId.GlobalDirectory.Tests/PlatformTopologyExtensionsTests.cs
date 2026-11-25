// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class PlatformTopologyExtensionsTests : BaseTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void HoldsOnlyControlPlaneTenant_WithTheTopology_IsTrueOnlyWhenTheCellIsTheControlPlaneCellOfAPlatformWithADirectory(
        bool hasDirectory,
        bool isControlPlaneCell,
        bool expected
    )
    {
        var topologyMock = CreateStrictMock<IPlatformTopology>();
        topologyMock.Setup(x => x.HasDirectory).Returns(hasDirectory).Verifiable(Times.Once);
        topologyMock
            .Setup(x => x.IsControlPlaneCell)
            .Returns(isControlPlaneCell)
            .Verifiable(hasDirectory ? Times.Once() : Times.Never());

        var result = topologyMock.Object.HoldsOnlyControlPlaneTenant();

        Assert.Equal(expected, result);
    }
}
