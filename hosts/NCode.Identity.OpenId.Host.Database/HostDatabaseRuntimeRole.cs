// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Resolves the name of the Postgres role that the reference OP host runs as, from configuration. The migration worker
/// grants the role its data access after it migrates, and the role name is judged in one place.
/// </summary>
/// <remarks>
/// The role is configured under <c>HostDatabase:RuntimeRole</c>. It is required, and it matches the pattern
/// <c>^[a-z_][a-z0-9_]*$</c> so it is safe to quote as an identifier in a statement.
/// </remarks>
// See ADR-1018. The migration worker is the MigrationService project.
[PublicAPI]
public static partial class HostDatabaseRuntimeRole
{
    /// <summary>
    /// The configuration key that names the runtime role, <c>HostDatabase:RuntimeRole</c>.
    /// </summary>
    public const string ConfigurationKey = "HostDatabase:RuntimeRole";

    /// <summary>
    /// Determines whether <paramref name="role"/> is a valid runtime role name: it matches <c>^[a-z_][a-z0-9_]*$</c>.
    /// </summary>
    /// <param name="role">The role name to judge.</param>
    /// <returns><see langword="true"/> when the name is valid; otherwise, <see langword="false"/>.</returns>
    public static bool IsValid(string role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return RuntimeRolePattern().IsMatch(role);
    }

    /// <summary>
    /// Resolves the runtime role name from <paramref name="configuration"/>.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <returns>The trimmed role name.</returns>
    /// <exception cref="InvalidOperationException">
    /// The role is missing, empty or not a valid role name. The message names <see cref="ConfigurationKey"/> and never
    /// holds the configured value.
    /// </exception>
    public static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var role = configuration[ConfigurationKey]?.Trim();
        if (string.IsNullOrEmpty(role))
        {
            throw new InvalidOperationException(
                $"The runtime role is not configured; set '{ConfigurationKey}' to the name of the database role the host runs as."
            );
        }

        if (!IsValid(role))
        {
            throw new InvalidOperationException(
                $"The '{ConfigurationKey}' value is not a valid role name; it must match ^[a-z_][a-z0-9_]*$."
            );
        }

        return role;
    }

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeRolePattern();
}
