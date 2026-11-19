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

    // GetOrAdd may run its factory more than once under contention, so each value is a Lazy and only the stored one
    // launches.
    private ConcurrentDictionary<string, Lazy<Task<IBrowser>>> Browsers { get; } = new();

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

        var launch = Browsers.GetOrAdd(
            browserName,
            name => new Lazy<Task<IBrowser>>(() =>
                Playwright[name].LaunchAsync(new BrowserTypeLaunchOptions { Headless = true })
            )
        );

        // The token ends this caller's wait, never the launch other callers share.
        var browser = await launch.Value.WaitAsync(cancellationToken);
        return await browser.NewContextAsync(
            new BrowserNewContextOptions { IgnoreHTTPSErrors = true }
        );
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var browserStops = Browsers
            .Values.Where(launch => launch.IsValueCreated)
            .Select(launch =>
                (Func<ValueTask>)(
                    async () =>
                    {
                        await ((Task)launch.Value).ConfigureAwait(
                            ConfigureAwaitOptions.SuppressThrowing
                        );

                        // A launch that failed holds no browser.
                        if (launch.Value.IsCompletedSuccessfully)
                        {
                            await (await launch.Value).DisposeAsync();
                        }
                    }
                )
            )
            .ToArray();

        Func<ValueTask> stopPlaywright = () =>
        {
            Browsers.Clear();
            Playwright.Dispose();
            return ValueTask.CompletedTask;
        };

        await Teardown.StopAllAsync([.. browserStops, stopPlaywright]);
    }
}
