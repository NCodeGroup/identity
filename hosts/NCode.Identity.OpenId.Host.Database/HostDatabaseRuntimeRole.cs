// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Resolves the name of the Postgres role that the reference OP host runs as, from configuration. The migration worker
/// grants the role its data access after it migrates, and the role name is judged in one place.
/// </summary>
/// <remarks>
/// The role is configured under <c>HostDatabase:RuntimeRole</c>. It is required, and it matches the pattern
/// <c>^[a-z_][a-z0-9_]*$</c> so it is safe to quote as an identifier in a statement. The <c>HostDatabase</c> section
/// binds strictly to <see cref="HostDatabaseSettings"/>, so a key that is not one of its properties stops the host.
/// </remarks>
// See ADR-1018. The migration worker is the MigrationService project.
[PublicAPI]
public static partial class HostDatabaseRuntimeRole
{
    /// <summary>
    /// The name of the configuration section that holds the runtime role, <c>HostDatabase</c>.
    /// </summary>
    public const string SectionName = "HostDatabase";

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
    /// The section holds a key that is not one of the <see cref="HostDatabaseSettings"/> properties, or the role is
    /// missing, empty or not a valid role name. The message names <c>HostDatabase:RuntimeRole</c> and never holds the
    /// configured value.
    /// </exception>
    public static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = new HostDatabaseSettings();
        configuration.GetSection(SectionName).BindStrictly(settings);

        var key = $"{SectionName}:{nameof(HostDatabaseSettings.RuntimeRole)}";

        var role = settings.RuntimeRole?.Trim();
        if (string.IsNullOrEmpty(role))
        {
            throw new InvalidOperationException(
                $"The runtime role is not configured; set '{key}' to the name of the database role the host runs as."
            );
        }

        if (!IsValid(role))
        {
            throw new InvalidOperationException(
                $"The '{key}' value is not a valid role name; it must match ^[a-z_][a-z0-9_]*$."
            );
        }

        return role;
    }

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeRolePattern();
}
