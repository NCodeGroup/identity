// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Opens pages and typeface licenses in a browser, and reports whether a page's typefaces load under its
/// Content-Security-Policy and what the browser received for each license.
/// </summary>
public static class TypefaceProbe
{
    /// <summary>
    /// The family name of the Instrument Sans typeface.
    /// </summary>
    public const string InstrumentSans = "Instrument Sans Variable";

    /// <summary>
    /// The family name of the Martian Mono typeface.
    /// </summary>
    public const string MartianMono = "Martian Mono Variable";

    private const int SettleMilliseconds = 5_000;

    private const int PollMilliseconds = 50;

    private const string RecordViolationsScript = """
        window.__policyViolations = []; document.addEventListener('securitypolicyviolation', (event) => window.__policyViolations.push(`${event.violatedDirective} ${event.blockedURI}`));
        """;

    private const string RenderedScript = """
        () => (document.getElementById('root')?.childElementCount ?? 0) > 0
        """;

    private const string DeadlineScript = """
        (milliseconds) => performance.now() + milliseconds
        """;

    private const string LoadFamilyScript = """
        async ({ family, deadline }) => {
          const failures = (window.__fontFailures ??= []);
          const timedOut = new Promise((resolve) => setTimeout(resolve, Math.max(0, deadline - performance.now()), null));
          try {
            const faces = await Promise.race([document.fonts.load(`16px "${family}"`), timedOut]);
            if (faces === null) {
              failures.push(`${family}: its load did not finish within the probe's bound`);
              return 0;
            }
            if (faces.length === 0) {
              failures.push(`${family}: its load matched no face`);
            }
            return faces.length;
          } catch (error) {
            failures.push(`${family}: its load failed: ${error}`);
            return 0;
          }
        }
        """;

    private const string SettledFamiliesScript = """
        async ({ families, deadline, pollMilliseconds }) => {
          const failures = (window.__fontFailures ??= []);
          const pause = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));
          const settle = () =>
            Promise.all(families.map((family) => document.fonts.load(`16px "${family}"`))).then(() => document.fonts.ready);
          const unchecked = () => families.filter((family) => !document.fonts.check(`16px "${family}"`));
          for (;;) {
            try {
              await Promise.race([settle(), pause(Math.max(0, deadline - performance.now()))]);
            } catch (error) {
              failures.push(`the font set failed to settle: ${error}`);
              return false;
            }
            const missing = unchecked();
            if (missing.length === 0) {
              return true;
            }
            const remaining = deadline - performance.now();
            if (remaining <= 0) {
              failures.push(`the font set did not report ${missing.join(" and ")} loaded within the probe's bound`);
              return false;
            }
            await pause(Math.min(pollMilliseconds, remaining));
          }
        }
        """;

    private const string ViolationsScript = """
        () => window.__policyViolations
        """;

    private const string FontFailuresScript = """
        () => window.__fontFailures ?? []
        """;

    /// <summary>
    /// Opens a page, waits for its bundle to render, loads both typefaces through the page's <c>FontFaceSet</c> and
    /// waits for it to settle, all within five seconds, and returns what the page fetched, what its policy refused,
    /// what its font set loaded and why a typeface did not load.
    /// </summary>
    /// <param name="context">The browser context to open the page in.</param>
    /// <param name="url">The URL of the page.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public static async ValueTask<TypefaceProbeResult> OpenAsync(
        IBrowserContext context,
        string url,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(url);
        cancellationToken.ThrowIfCancellationRequested();

        // The init script runs before the page's own scripts, so it records every violation of the page.
        await context.AddInitScriptAsync(RecordViolationsScript);

        var page = await context.NewPageAsync();

        var fontResponses = new ConcurrentQueue<FontResponse>();
        page.Response += (_, response) =>
        {
            if (response.Url.EndsWith(".woff2", StringComparison.Ordinal))
            {
                var fontUrl = new Uri(response.Url);
                var contentType = response.Headers.GetValueOrDefault("content-type");
                var fontResponse = new FontResponse(fontUrl, response.Status, contentType);
                fontResponses.Enqueue(fontResponse);
            }
        };

        var policyMessages = new ConcurrentQueue<string>();
        page.Console += (_, message) =>
        {
            if (
                message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase)
                || message.Text.Contains(
                    "Content-Security-Policy",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                policyMessages.Enqueue(message.Text);
            }
        };

        var gotoOptions = new PageGotoOptions { WaitUntil = WaitUntilState.Load };
        await page.GotoAsync(url, gotoOptions);
        await page.WaitForFunctionAsync(RenderedScript);

        var deadline = await page.EvaluateAsync<double>(DeadlineScript, SettleMilliseconds);

        var instrumentSansLoad = new { family = InstrumentSans, deadline };
        var instrumentSansFaces = await page.EvaluateAsync<int>(
            LoadFamilyScript,
            instrumentSansLoad
        );
        var martianMonoLoad = new { family = MartianMono, deadline };
        var martianMonoFaces = await page.EvaluateAsync<int>(LoadFamilyScript, martianMonoLoad);

        // WebKit can report a family unloaded right after its load resolves, so read the set once it settles.
        string[] families = [InstrumentSans, MartianMono];
        var settle = new
        {
            families,
            deadline,
            pollMilliseconds = PollMilliseconds,
        };
        var bothChecked = await page.EvaluateAsync<bool>(SettledFamiliesScript, settle);
        var policyViolations = await page.EvaluateAsync<string[]>(ViolationsScript);
        var fontFailures = await page.EvaluateAsync<string[]>(FontFailuresScript);

        return new TypefaceProbeResult(
            [.. fontResponses],
            policyViolations,
            [.. policyMessages],
            instrumentSansFaces,
            martianMonoFaces,
            bothChecked,
            fontFailures
        );
    }

    /// <summary>
    /// Opens each typeface license in a page, one after another, and returns the browser's response to each.
    /// </summary>
    /// <param name="context">The browser context to open the page in.</param>
    /// <param name="urls">The addresses of the licenses.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public static async ValueTask<IReadOnlyList<LicenseResponse>> OpenLicensesAsync(
        IBrowserContext context,
        IReadOnlyList<Uri> urls,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(urls);
        cancellationToken.ThrowIfCancellationRequested();

        var page = await context.NewPageAsync();

        List<LicenseResponse> responses = [];
        foreach (var url in urls)
        {
            var address = url.AbsoluteUri;
            var response =
                await page.GotoAsync(address)
                ?? throw new InvalidOperationException(
                    $"Opening '{address}' returned no response."
                );
            var text = await response.TextAsync();
            var contentType = response.Headers.GetValueOrDefault("content-type");
            var responseUrl = new Uri(response.Url);
            var licenseResponse = new LicenseResponse(
                responseUrl,
                response.Status,
                contentType,
                text
            );
            responses.Add(licenseResponse);
        }

        return responses;
    }
}
