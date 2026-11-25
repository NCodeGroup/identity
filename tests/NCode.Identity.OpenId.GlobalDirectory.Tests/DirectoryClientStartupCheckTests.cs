// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.OpenId.Client;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryClientStartupCheckTests : BaseTests
{
    private static DirectoryClientStartupCheck CreateCheck(
        string? directoryUrl,
        string? issuer,
        string? clientId = "cell-eu-1",
        string managementUrl = "https://manage.eu-1.example",
        bool isControlPlane = false,
        string? keyRing = "cell-client",
        bool hasKeyRing = true
    )
    {
        var keyRingProvider = new Mock<IClientKeyRingProvider>();
        keyRingProvider.Setup(x => x.HasKeyRing(It.IsAny<string>())).Returns(hasKeyRing);
        return new(
            Options.Create(
                new CellOptions
                {
                    Name = "eu-1",
                    ManagementUrl = managementUrl,
                    IsControlPlane = isControlPlane,
                    ControlPlane = new ControlPlaneOptions { Issuer = issuer },
                }
            ),
            Options.Create(
                new DirectoryClientOptions
                {
                    Url = directoryUrl,
                    ClientId = clientId,
                    KeyRing = keyRing,
                }
            ),
            keyRingProvider.Object
        );
    }

    [Fact]
    public async Task StartingAsync_DirectoryUrlAndUnknownKeyRing_ThrowsNamingTheSetting()
    {
        var check = CreateCheck(
            "https://directory.example",
            "https://issuer.example",
            hasKeyRing: false
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            check.StartingAsync(CancellationToken.None)
        );

        Assert.Contains("Directory:KeyRing", exception.Message);
    }

    [Fact]
    public async Task StartingAsync_ControlPlaneCellWithoutIssuer_Completes()
    {
        var check = CreateCheck("https://directory.example", null, isControlPlane: true);

        await check.StartingAsync(CancellationToken.None);
    }

    #region StartingAsync Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("http://issuer.example")]
    [InlineData("https://issuer.example")]
    public async Task StartingAsync_NoDirectoryUrl_Completes(string? issuer)
    {
        var check = CreateCheck(directoryUrl: null, issuer);

        await check.StartingAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("https://issuer.example")]
    [InlineData("http://127.0.0.1:5000")]
    public async Task StartingAsync_DirectoryUrlAndAllowedIssuer_Completes(string issuer)
    {
        var check = CreateCheck("https://directory.example", issuer);

        await check.StartingAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("http://issuer.example")]
    [InlineData("https://issuer.example/#")]
    [InlineData("https://@issuer.example/")]
    public async Task StartingAsync_DirectoryUrlAndDisallowedIssuer_ThrowsNamingTheValue(
        string issuer
    )
    {
        var check = CreateCheck("https://directory.example", issuer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            check.StartingAsync(CancellationToken.None)
        );

        Assert.Contains("Cell:ControlPlane:Issuer", exception.Message);
        Assert.Contains($"'{DirectoryUrls.Describe(issuer)}'", exception.Message);
    }

    [Fact]
    public async Task StartingAsync_DirectoryUrlAndIssuerWithUserInformation_ThrowsWithoutEchoingTheCredentials()
    {
        var check = CreateCheck(
            "https://directory.example",
            "https://user:secret@issuer.example/path?q=x"
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            check.StartingAsync(CancellationToken.None)
        );

        Assert.Contains("Cell:ControlPlane:Issuer", exception.Message);
        Assert.Contains("https://issuer.example/path", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.DoesNotContain("q=x", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cell-eu-2")]
    [InlineData("CELL-EU-1")]
    public async Task StartingAsync_DirectoryUrlAndAnotherClientId_ThrowsNamingTheSettings(
        string? clientId
    )
    {
        var check = CreateCheck("https://directory.example", "https://issuer.example", clientId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            check.StartingAsync(CancellationToken.None)
        );

        Assert.Contains("Directory:ClientId", exception.Message);
        Assert.Contains("Cell:Name", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task StartingAsync_DirectoryUrlAndNoManagementUrl_ThrowsNamingTheSetting(
        string managementUrl
    )
    {
        var check = CreateCheck(
            "https://directory.example",
            "https://issuer.example",
            managementUrl: managementUrl
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            check.StartingAsync(CancellationToken.None)
        );

        Assert.Contains("Cell:ManagementUrl", exception.Message);
    }

    [Fact]
    public async Task StartingAsync_NoDirectoryUrlAndNoCellIdentity_Completes()
    {
        var check = CreateCheck(
            directoryUrl: null,
            "https://issuer.example",
            clientId: null,
            managementUrl: ""
        );

        await check.StartingAsync(CancellationToken.None);
    }

    #endregion

    #region Other Lifecycle Tests

    [Fact]
    public async Task OtherLifecycleMethods_Always_Complete()
    {
        var check = CreateCheck("https://directory.example", "relative");

        await check.StartAsync(CancellationToken.None);
        await check.StartedAsync(CancellationToken.None);
        await check.StoppingAsync(CancellationToken.None);
        await check.StopAsync(CancellationToken.None);
        await check.StoppedAsync(CancellationToken.None);
    }

    #endregion
}
