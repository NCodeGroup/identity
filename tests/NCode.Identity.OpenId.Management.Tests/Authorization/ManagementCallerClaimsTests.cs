// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.Management.Authorization;

public class ManagementCallerClaimsTests
{
    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void IsCellIssued_WhenTheCellIssuedTheToken_ReturnsTrue()
    {
        var principal = CreatePrincipal(
            new Claim(ManagementIssuerKinds.ClaimType, ManagementIssuerKinds.Cell)
        );

        Assert.True(principal.IsCellIssued);
    }

    [Fact]
    public void IsCellIssued_WhenTheControlPlaneIssuedTheToken_ReturnsFalse()
    {
        var principal = CreatePrincipal(
            new Claim(ManagementIssuerKinds.ClaimType, ManagementIssuerKinds.ControlPlane)
        );

        Assert.False(principal.IsCellIssued);
    }

    [Fact]
    public void IsCellIssued_WhenTheClaimIsMissing_ReturnsFalse()
    {
        Assert.False(CreatePrincipal().IsCellIssued);
    }

    [Fact]
    public void IsCellIssued_WhenTheClaimDiffersInCase_ReturnsFalse()
    {
        var principal = CreatePrincipal(
            new Claim(
                ManagementIssuerKinds.ClaimType,
                ManagementIssuerKinds.Cell.ToUpperInvariant()
            )
        );

        Assert.False(principal.IsCellIssued);
    }
}
