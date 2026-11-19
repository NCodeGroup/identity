// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// A typeface license's response as the browser received it.
/// </summary>
/// <param name="Url">The address that the browser received the response from.</param>
/// <param name="Status">The HTTP status code of the response.</param>
/// <param name="ContentType">The <c>Content-Type</c> header of the response, or <see langword="null"/> when it has
/// none.</param>
/// <param name="Text">The body of the response as text.</param>
public sealed record LicenseResponse(Uri Url, int Status, string? ContentType, string Text);
