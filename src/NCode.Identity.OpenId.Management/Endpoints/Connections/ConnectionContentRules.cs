// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence;

namespace NCode.Identity.OpenId.Management.Endpoints.Connections;

/// <summary>
/// Holds the rules for a connection's name and display name, which the connection endpoints and the tenant promotion
/// apply alike.
/// </summary>
internal static class ConnectionContentRules
{
    /// <summary>
    /// Gets the description of how the specified name breaks the connection name rule.
    /// </summary>
    /// <param name="name">The connection name to check.</param>
    /// <returns>The error description, or <see langword="null"/> when the name is valid.</returns>
    public static string? GetNameErrorOrDefault(string name) =>
        ConnectionNameRules.IsValid(name)
            ? null
            : $"The connection name must be 1 to {ConnectionNameRules.MaxLength} ASCII letters, digits, '-' or '_', starting with a letter or digit.";

    /// <summary>
    /// Gets the description of how the specified display name breaks the display name rule.
    /// </summary>
    /// <param name="displayName">The display name to check.</param>
    /// <returns>The error description, or <see langword="null"/> when the display name is valid.</returns>
    public static string? GetDisplayNameErrorOrDefault(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName)
        || displayName.Length > OpenIdMaxLengths.DisplayName
        || displayName.Any(char.IsControl)
            ? $"The display name must not be empty, longer than {OpenIdMaxLengths.DisplayName} characters or contain a control character."
            : null;
}
