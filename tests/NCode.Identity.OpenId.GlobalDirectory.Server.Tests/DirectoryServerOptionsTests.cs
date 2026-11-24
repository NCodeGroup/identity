// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryServerOptionsTests
{
    [Fact]
    public void Issuer_ByDefault_IsEmpty()
    {
        var options = new DirectoryServerOptions();

        Assert.Equal(string.Empty, options.Issuer);
    }

    [Fact]
    public void KeyRing_ByDefault_IsEmpty()
    {
        var options = new DirectoryServerOptions();

        Assert.Equal(string.Empty, options.KeyRing);
    }

    [Fact]
    public void TokenLifetime_ByDefault_IsFiveMinutes()
    {
        var options = new DirectoryServerOptions();

        Assert.Equal(TimeSpan.FromMinutes(5), options.TokenLifetime);
    }

    [Fact]
    public void ControlPlane_ByDefault_IsEmptyWithTheSharedKind()
    {
        var controlPlane = new DirectoryServerOptions().ControlPlane;

        Assert.Equal(string.Empty, controlPlane.Cell);
        Assert.Equal(string.Empty, controlPlane.Region);
        Assert.Equal(string.Empty, controlPlane.TenantId);
        Assert.Equal(string.Empty, controlPlane.Issuer);
        Assert.Equal(string.Empty, controlPlane.ClientJwks);
        Assert.Equal(CellKinds.Shared, controlPlane.Kind);
    }
}
