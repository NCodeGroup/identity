// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryServerOptionsTests
{
    #region Issuer Property Tests

    [Fact]
    public void Issuer_ByDefault_IsEmpty()
    {
        var options = new DirectoryServerOptions();

        Assert.Equal(string.Empty, options.Issuer);
    }

    #endregion

    #region ControlPlaneCell Property Tests

    [Fact]
    public void ControlPlaneCell_ByDefault_IsEmpty()
    {
        var options = new DirectoryServerOptions();

        Assert.Equal(string.Empty, options.ControlPlaneCell);
    }

    #endregion
}
