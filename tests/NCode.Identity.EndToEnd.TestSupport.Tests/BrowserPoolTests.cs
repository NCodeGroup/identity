// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Playwright;
using Moq;
using Xunit;

namespace NCode.Identity.EndToEnd.TestSupport.Tests;

public sealed class BrowserPoolTests : BaseTests
{
    #region NewContextAsync Tests

    [Fact]
    public async Task NewContextAsync_WhenCallersAskForOneEngineAtOnce_LaunchesItsBrowserOnce()
    {
        var launch = new TaskCompletionSource<IBrowser>();
        var context = CreateStrictMock<IBrowserContext>();
        var browser = CreateStrictMock<IBrowser>();
        browser
            .Setup(x =>
                x.NewContextAsync(It.Is<BrowserNewContextOptions>(o => o.IgnoreHTTPSErrors == true))
            )
            .ReturnsAsync(context.Object)
            .Verifiable();
        browser.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        var browserType = CreateStrictMock<IBrowserType>();
        browserType
            .Setup(x => x.LaunchAsync(It.Is<BrowserTypeLaunchOptions>(o => o.Headless == true)))
            .Returns(launch.Task)
            .Verifiable();
        var playwright = CreateStrictMock<IPlaywright>();
        playwright.Setup(x => x[BrowserType.Chromium]).Returns(browserType.Object).Verifiable();
        playwright.Setup(x => x.Dispose()).Verifiable();
        await using var pool = new BrowserPool(playwright.Object);

        var first = pool.NewContextAsync(BrowserType.Chromium, CancellationToken.None).AsTask();
        var second = pool.NewContextAsync(BrowserType.Chromium, CancellationToken.None).AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        launch.SetResult(browser.Object);

        var firstContext = await first;
        var secondContext = await second;

        Assert.Same(context.Object, firstContext);
        Assert.Same(context.Object, secondContext);
        browserType.Verify(x => x.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>()), Times.Once());
    }

    [Fact]
    public async Task NewContextAsync_WhenACallersWaitIsCancelled_LeavesTheLaunchToTheOtherCallers()
    {
        var launch = new TaskCompletionSource<IBrowser>();
        var context = CreateStrictMock<IBrowserContext>();
        var browser = CreateStrictMock<IBrowser>();
        browser
            .Setup(x => x.NewContextAsync(It.IsAny<BrowserNewContextOptions>()))
            .ReturnsAsync(context.Object)
            .Verifiable();
        browser.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        var browserType = CreateStrictMock<IBrowserType>();
        browserType
            .Setup(x => x.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>()))
            .Returns(launch.Task)
            .Verifiable();
        var playwright = CreateStrictMock<IPlaywright>();
        playwright.Setup(x => x[BrowserType.Firefox]).Returns(browserType.Object).Verifiable();
        playwright.Setup(x => x.Dispose()).Verifiable();
        await using var pool = new BrowserPool(playwright.Object);
        using var cancellation = new CancellationTokenSource();

        var cancelled = pool.NewContextAsync(BrowserType.Firefox, cancellation.Token).AsTask();
        var waiting = pool.NewContextAsync(BrowserType.Firefox, CancellationToken.None).AsTask();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(waiting.IsCompleted);

        launch.SetResult(browser.Object);

        var waitingContext = await waiting;

        Assert.Same(context.Object, waitingContext);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_WhenALaunchFailed_DisposesTheBrowsersThatLaunchedAndPlaywright()
    {
        var launchFailure = new InvalidOperationException("launch");
        var context = CreateStrictMock<IBrowserContext>();
        var browser = CreateStrictMock<IBrowser>();
        browser
            .Setup(x => x.NewContextAsync(It.IsAny<BrowserNewContextOptions>()))
            .ReturnsAsync(context.Object)
            .Verifiable();
        browser.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        var chromium = CreateStrictMock<IBrowserType>();
        chromium
            .Setup(x => x.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>()))
            .ReturnsAsync(browser.Object)
            .Verifiable();
        var firefox = CreateStrictMock<IBrowserType>();
        firefox
            .Setup(x => x.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>()))
            .ThrowsAsync(launchFailure)
            .Verifiable();
        var playwright = CreateStrictMock<IPlaywright>();
        playwright.Setup(x => x[BrowserType.Chromium]).Returns(chromium.Object).Verifiable();
        playwright.Setup(x => x[BrowserType.Firefox]).Returns(firefox.Object).Verifiable();
        playwright.Setup(x => x.Dispose()).Verifiable();
        var pool = new BrowserPool(playwright.Object);

        await pool.NewContextAsync(BrowserType.Chromium, CancellationToken.None);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pool.NewContextAsync(BrowserType.Firefox, CancellationToken.None)
        );

        Assert.Same(launchFailure, failure);

        await pool.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WhenABrowserFailsToClose_DisposesPlaywrightAndReportsTheFailure()
    {
        var closeFailure = new InvalidOperationException("close");
        var context = CreateStrictMock<IBrowserContext>();
        var browser = CreateStrictMock<IBrowser>();
        browser
            .Setup(x => x.NewContextAsync(It.IsAny<BrowserNewContextOptions>()))
            .ReturnsAsync(context.Object)
            .Verifiable();
        browser.Setup(x => x.DisposeAsync()).ThrowsAsync(closeFailure).Verifiable();
        var chromium = CreateStrictMock<IBrowserType>();
        chromium
            .Setup(x => x.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>()))
            .ReturnsAsync(browser.Object)
            .Verifiable();
        var playwright = CreateStrictMock<IPlaywright>();
        playwright.Setup(x => x[BrowserType.Chromium]).Returns(chromium.Object).Verifiable();
        playwright.Setup(x => x.Dispose()).Verifiable();
        var pool = new BrowserPool(playwright.Object);

        await pool.NewContextAsync(BrowserType.Chromium, CancellationToken.None);

        var failure = await Assert.ThrowsAsync<AggregateException>(async () =>
            await pool.DisposeAsync()
        );

        var reported = Assert.Single(failure.InnerExceptions);

        Assert.Same(closeFailure, reported);
    }

    #endregion
}
