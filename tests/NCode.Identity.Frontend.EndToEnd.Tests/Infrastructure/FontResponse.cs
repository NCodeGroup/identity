// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// A font file's response as the browser received it.
/// </summary>
/// <param name="Url">The URL that the browser requested.</param>
/// <param name="Status">The HTTP status code of the response.</param>
/// <param name="ContentType">The <c>Content-Type</c> header of the response, or <c>null</c> when it has none.</param>
public sealed record FontResponse(Uri Url, int Status, string? ContentType);
