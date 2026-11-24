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

    /// <summary>
    /// Gets the connections the form offers, as name and display name, or none for a form that carries no connection.
    /// </summary>
    public IReadOnlyList<(string Name, string DisplayName)> Connections { get; init; } = [];

    /// <summary>
    /// Gets the name of the connection the form preselects, or <see langword="null"/> for none.
    /// </summary>
    public string? SelectedConnection { get; init; }

    /// <summary>
    /// Gets the address of the forgot-password page the form links to, or <see langword="null"/> for no link.
    /// </summary>
    public string? ForgotPasswordHref { get; init; }
}
