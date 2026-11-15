// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryClientStartupCheckTests : BaseTests
{
    private static DirectoryClientStartupCheck CreateCheck(string? directoryUrl, string? issuer) =>
        new(
            Options.Create(
                new CellOptions { ControlPlane = new ControlPlaneOptions { Issuer = issuer } }
            ),
            Options.Create(new DirectoryClientOptions { Url = directoryUrl })
        );

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
