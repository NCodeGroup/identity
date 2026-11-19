// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace NCode.Identity.EndToEnd.TestSupport;

/// <summary>
/// Launches one headless browser per engine for a test run and creates the browser contexts that tests use.
/// </summary>
public sealed class BrowserPool : IAsyncDisposable
{
    private BrowserPool(IPlaywright playwright)
    {
        Playwright = playwright;
    }

    private IPlaywright Playwright { get; }

    private ConcurrentDictionary<string, Task<IBrowser>> Browsers { get; } = new();

    /// <summary>
    /// Starts Playwright.
    /// </summary>
    public static async ValueTask<BrowserPool> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new BrowserPool(await Microsoft.Playwright.Playwright.CreateAsync());
    }

    /// <summary>
    /// Creates a browser context that ignores certificate errors, in a browser that is launched once per run.
    /// </summary>
    public async ValueTask<IBrowserContext> NewContextAsync(
        string browserName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var browser = await Browsers.GetOrAdd(
            browserName,
            name => Playwright[name].LaunchAsync(new BrowserTypeLaunchOptions { Headless = true })
        );
        return await browser.NewContextAsync(
            new BrowserNewContextOptions { IgnoreHTTPSErrors = true }
        );
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var browser in Browsers.Values)
        {
            await (await browser).DisposeAsync();
        }

        Browsers.Clear();

        Playwright.Dispose();
    }
}
