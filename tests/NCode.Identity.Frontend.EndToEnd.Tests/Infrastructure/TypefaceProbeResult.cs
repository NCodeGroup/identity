// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// What a page fetched and what its Content-Security-Policy refused while it loaded the typefaces.
/// </summary>
/// <param name="FontResponses">The responses to every <c>.woff2</c> request that the page made.</param>
/// <param name="PolicyViolations">The directive and blocked URI of every <c>securitypolicyviolation</c> event.</param>
/// <param name="PolicyMessages">The text of every console message that mentions the Content-Security-Policy.</param>
/// <param name="InstrumentSansFaces">The number of Instrument Sans font faces that the page's font set loaded.</param>
/// <param name="MartianMonoFaces">The number of Martian Mono font faces that the page's font set loaded.</param>
/// <param name="BothChecked">Whether the page's font set reports both typefaces as loaded.</param>
public sealed record TypefaceProbeResult(
    IReadOnlyList<FontResponse> FontResponses,
    IReadOnlyList<string> PolicyViolations,
    IReadOnlyList<string> PolicyMessages,
    int InstrumentSansFaces,
    int MartianMonoFaces,
    bool BothChecked
);
