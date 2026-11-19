// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Opens a page in a browser and reports whether its typefaces load under its Content-Security-Policy.
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

    private const string RecordViolationsScript = """
        window.__policyViolations = []; document.addEventListener('securitypolicyviolation', (event) => window.__policyViolations.push(`${event.violatedDirective} ${event.blockedURI}`));
        """;

    private const string RenderedScript = """
        () => (document.getElementById('root')?.childElementCount ?? 0) > 0
        """;

    private const string LoadFamilyScript = """
        async (family) => (await document.fonts.load(`16px "${family}"`)).length
        """;

    private const string CheckFamiliesScript = """
        (families) => families.every((family) => document.fonts.check(`16px "${family}"`))
        """;

    private const string ViolationsScript = """
        () => window.__policyViolations
        """;

    /// <summary>
    /// Opens a page, waits for its bundle to render, loads both typefaces through the page's <c>FontFaceSet</c>, and
    /// returns what the page fetched and what its policy refused.
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
                fontResponses.Enqueue(
                    new FontResponse(
                        new Uri(response.Url),
                        response.Status,
                        response.Headers.GetValueOrDefault("content-type")
                    )
                );
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

        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
        await page.WaitForFunctionAsync(RenderedScript);

        var instrumentSansFaces = await page.EvaluateAsync<int>(LoadFamilyScript, InstrumentSans);
        var martianMonoFaces = await page.EvaluateAsync<int>(LoadFamilyScript, MartianMono);

        string[] families = [InstrumentSans, MartianMono];
        var bothChecked = await page.EvaluateAsync<bool>(CheckFamiliesScript, families);
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
}
