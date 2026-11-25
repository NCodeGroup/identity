// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Tenants;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class ManagementUrlStartupCheckTests
{
    private const string RootTenantId = "root";

    private static ManagementUrlStartupCheck CreateCheck(
        bool hasDirectory,
        bool isControlPlaneCell,
        bool hasManagementTenant,
        string managementUrl,
        string? issuer
    )
    {
        var topology = Mock.Of<IPlatformTopology>(x =>
            x.HasDirectory == hasDirectory
            && x.IsControlPlaneCell == isControlPlaneCell
            && x.HasManagementTenant == hasManagementTenant
        );

        var resolver = new Mock<ITenantResolver>();
        resolver.Setup(x => x.GetControlPlaneTenantId()).Returns(RootTenantId);
        resolver
            .Setup(x => x.GetDefaultIssuerAsync(RootTenantId, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<string?>(issuer));

        var options = Options.Create(new CellOptions { ManagementUrl = managementUrl });
        return new ManagementUrlStartupCheck(topology, options, resolver.Object);
    }

    [Fact]
    public async Task StartAsync_WithoutADirectory_Completes()
    {
        var check = CreateCheck(
            false,
            true,
            false,
            "https://other.example",
            "https://auth.example"
        );

        await check.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WorkloadCellWithoutAManagementTenant_Completes()
    {
        var check = CreateCheck(
            true,
            false,
            false,
            "https://other.example",
            "https://auth.example"
        );

        await check.StartAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task StartAsync_WhenTheOriginsMatch_Completes(
        bool isControlPlaneCell,
        bool hasManagementTenant
    )
    {
        var check = CreateCheck(
            true,
            isControlPlaneCell,
            hasManagementTenant,
            "https://AUTH.example/api",
            "https://auth.example/root"
        );

        await check.StartAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task StartAsync_WhenTheOriginsDiffer_Throws(
        bool isControlPlaneCell,
        bool hasManagementTenant
    )
    {
        var check = CreateCheck(
            true,
            isControlPlaneCell,
            hasManagementTenant,
            "https://manage.example",
            "https://auth.example"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            check.StartAsync(CancellationToken.None)
        );
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("https://auth.example/api?x=1")]
    [InlineData("https://auth.example/ api")]
    [InlineData("https://user@auth.example/api")]
    public async Task StartAsync_WhenTheManagementUrlIsNotAServiceUrl_Throws(string managementUrl)
    {
        var check = CreateCheck(true, true, false, managementUrl, "https://auth.example");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            check.StartAsync(CancellationToken.None)
        );
    }
}
