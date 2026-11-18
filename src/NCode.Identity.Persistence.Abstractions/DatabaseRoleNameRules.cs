// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace NCode.Identity.Persistence;

/// <summary>
/// Contains the rule for the Postgres role names that the hosts run as: lowercase letters, digits and underscores that
/// do not start with a digit, so a name is safe to quote as an identifier.
/// </summary>
/// <remarks>
/// The host chart repeats the pattern in its value schema and templates, and the copies change together.
/// </remarks>
[PublicAPI]
public static partial class DatabaseRoleNameRules
{
    /// <summary>
    /// The pattern that messages quote. <see cref="IsValid"/> is the check: it refuses a name with a trailing newline,
    /// which this pattern anchored with '$' would accept.
    /// </summary>
    public const string Pattern = "^[a-z_][a-z0-9_]*$";

    /// <summary>
    /// Gets the regular expression that matches a valid role name.
    /// </summary>
    /// <returns>The regular expression.</returns>
    // '\z' rather than '$', which also matches before a trailing newline.
    [GeneratedRegex(@"^[a-z_][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RoleRegex();

    /// <summary>
    /// Determines whether the specified value is a valid role name.
    /// </summary>
    /// <param name="role">The value to check.</param>
    /// <returns>
    /// <see langword="true"/> when the value is a valid role name; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool IsValid([NotNullWhen(true)] string? role) =>
        role is not null && RoleRegex().IsMatch(role);

    /// <summary>
    /// Trims the configured runtime role and requires it to be a valid role name.
    /// </summary>
    /// <param name="configuredRole">The configured value.</param>
    /// <param name="key">The configuration key that holds the value, which messages name.</param>
    /// <param name="connectsAs">
    /// The text that completes "the name of the database role", which a message quotes.
    /// </param>
    /// <returns>The trimmed role name.</returns>
    /// <exception cref="InvalidOperationException">The value is blank or is not a valid role name.</exception>
    public static string RequireRuntimeRole(string? configuredRole, string key, string connectsAs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectsAs);

        var role = configuredRole?.Trim();
        if (string.IsNullOrEmpty(role))
        {
            throw new InvalidOperationException(
                $"The runtime role is not configured; set '{key}' to the name of the database role {connectsAs}."
            );
        }

        if (!IsValid(role))
        {
            throw new InvalidOperationException(
                $"The '{key}' value is not a valid role name; it must match {Pattern}."
            );
        }

        return role;
    }
}
