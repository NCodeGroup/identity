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
          const timedOut = new Promise((resolve) => setTimeout(resolve, Math.max(0, deadline - performance.now()), []));
          try {
            return (await Promise.race([document.fonts.load(`16px "${family}"`), timedOut])).length;
          } catch {
            return 0;
          }
        }
        """;

    private const string SettledFamiliesScript = """
        async ({ families, deadline }) => {
          const pause = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));
          const settle = () =>
            Promise.all(families.map((family) => document.fonts.load(`16px "${family}"`))).then(() => document.fonts.ready);
          for (;;) {
            const remaining = deadline - performance.now();
            if (remaining <= 0) {
              return false;
            }
            try {
              await Promise.race([settle(), pause(remaining)]);
            } catch {
              return false;
            }
            if (families.every((family) => document.fonts.check(`16px "${family}"`))) {
              return true;
            }
            await pause(50);
          }
        }
        """;

    private const string ViolationsScript = """
        () => window.__policyViolations
        """;

    /// <summary>
    /// Opens a page, waits for its bundle to render, loads both typefaces through the page's <c>FontFaceSet</c> and
    /// waits for it to settle, all within five seconds, and returns what the page fetched, what its policy refused and
    /// what its font set loaded.
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

        var instrumentSansFaces = await page.EvaluateAsync<int>(
            LoadFamilyScript,
            new { family = InstrumentSans, deadline }
        );
        var martianMonoFaces = await page.EvaluateAsync<int>(
            LoadFamilyScript,
            new { family = MartianMono, deadline }
        );

        // WebKit can report a family unloaded right after its load resolves, so read the set once it settles.
        string[] families = [InstrumentSans, MartianMono];
        var bothChecked = await page.EvaluateAsync<bool>(
            SettledFamiliesScript,
            new { families, deadline }
        );
        var policyViolations = await page.EvaluateAsync<string[]>(ViolationsScript);

        return new TypefaceProbeResult(
            [.. fontResponses],
            policyViolations,
            [.. policyMessages],
            instrumentSansFaces,
            martianMonoFaces,
            bothChecked
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
