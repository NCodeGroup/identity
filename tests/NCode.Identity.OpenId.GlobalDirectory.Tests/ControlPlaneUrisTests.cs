// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.GlobalDirectory.ControlPlane;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class ControlPlaneUrisTests
{
    #region Create Tests

    [Fact]
    public void Create_WhenTheIssuerHasAPath_ReturnsThePathAtTheIssuersOrigin()
    {
        var cellOptions = new CellOptions
        {
            ControlPlane = new ControlPlaneOptions
            {
                Issuer = "https://auth.example.test/some/path",
            },
        };

        var result = ControlPlaneUris.Create(
            cellOptions,
            GlobalDirectoryProtocol.ControlPlanePrincipalsPath
        );

        Assert.Equal("https://auth.example.test/control-plane/v1/principals", result.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a uri")]
    public void Create_WhenTheIssuerIsMissingOrInvalid_ThrowsDirectoryUnavailable(string? issuer)
    {
        var cellOptions = new CellOptions
        {
            ControlPlane = new ControlPlaneOptions { Issuer = issuer },
        };

        var exception = Assert.Throws<DirectoryUnavailableException>(() =>
            ControlPlaneUris.Create(cellOptions, GlobalDirectoryProtocol.ControlPlaneAuthorityPath)
        );

        Assert.Equal("The cell does not know the issuer of the control plane.", exception.Message);
    }

    #endregion
}
