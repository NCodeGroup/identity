// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Antiforgery;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// The form a page of the interactive surface posts: the antiforgery token it echoes, the return URL it carries and the
/// messages shown above it.
/// </summary>
/// <param name="Antiforgery">The antiforgery token set the form echoes.</param>
internal sealed record InteractivePageForm(AntiforgeryTokenSet Antiforgery)
{
    /// <summary>
    /// Gets the URL the form carries so the request resumes once the form is posted.
    /// </summary>
    public string ReturnUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets the message shown above the form, or <see langword="null"/> for none.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the message shown above the form as a status, or <see langword="null"/> for none.
    /// </summary>
    public string? StatusMessage { get; init; }
}
