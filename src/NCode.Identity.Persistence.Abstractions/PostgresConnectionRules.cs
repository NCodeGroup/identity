// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Data.Common;
using System.Globalization;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using NCode.Identity.Registration;

namespace NCode.Identity.Persistence;

/// <summary>
/// Contains the rule that resolves a Postgres connection string from configuration. A host's database is configured in
/// one of two forms, and the application and its migration worker both resolve it here, so the two forms of one
/// connection are judged in one place.
/// </summary>
/// <remarks>
/// The connection is configured as exactly one of two forms: a finished connection string under
/// <c>ConnectionStrings:{name}</c>, or the parts under a configuration section (<c>Host</c>, <c>Port</c>, <c>Name</c>,
/// <c>Username</c> and <c>Password</c>, the values a Postgres operator writes to its Secret). The parts are assembled
/// with <see cref="DbConnectionStringBuilder"/>, so a password that holds <c>;</c>, <c>'</c> or <c>"</c> stays a
/// password. The section binds strictly, so a key that is not one of the parts stops the host. A message names
/// configuration paths and never holds a configured value.
/// </remarks>
// See ADR-1509.
[PublicAPI]
public static class PostgresConnectionRules
{
    /// <summary>
    /// Resolves the Postgres connection string from <paramref name="configuration"/>.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <param name="connectionStringName">
    /// The name of the connection string, which is read from <c>ConnectionStrings:{name}</c>.
    /// </param>
    /// <param name="sectionName">The name of the configuration section that holds the connection's parts.</param>
    /// <returns>The connection string, or <see langword="null"/> when neither form is configured.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="connectionStringName"/> or <paramref name="sectionName"/> is <see langword="null"/>, empty or
    /// white space.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Both forms are configured, the section holds a key that is not one of the parts, a required part is missing, or
    /// the port is not a number from 1 to 65535. The message names the configuration paths and never holds a configured
    /// value.
    /// </exception>
    public static string? Resolve(
        IConfiguration configuration,
        string connectionStringName,
        string sectionName
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var connectionString = configuration.GetConnectionString(connectionStringName);

        var parts = new PostgresConnectionParts();
        configuration.GetSection(sectionName).BindStrictly(parts);

        string?[] partValues = [parts.Host, parts.Port, parts.Name, parts.Username, parts.Password];

        var hasConnectionString = !string.IsNullOrEmpty(connectionString);
        var hasParts = partValues.Any(part => !string.IsNullOrEmpty(part));

        if (hasConnectionString && hasParts)
        {
            throw new InvalidOperationException(
                $"The connection string 'ConnectionStrings:{connectionStringName}' and the '{sectionName}' parts are "
                    + "both configured; configure exactly one of them."
            );
        }

        if (!hasParts)
            return hasConnectionString ? connectionString : null;

        return Assemble(parts, sectionName);
    }

    private static string Assemble(PostgresConnectionParts parts, string sectionName)
    {
        List<string> missing = [];
        if (string.IsNullOrEmpty(parts.Host))
            missing.Add($"{sectionName}:{nameof(PostgresConnectionParts.Host)}");
        if (string.IsNullOrEmpty(parts.Name))
            missing.Add($"{sectionName}:{nameof(PostgresConnectionParts.Name)}");
        if (string.IsNullOrEmpty(parts.Username))
            missing.Add($"{sectionName}:{nameof(PostgresConnectionParts.Username)}");
        if (string.IsNullOrEmpty(parts.Password))
            missing.Add($"{sectionName}:{nameof(PostgresConnectionParts.Password)}");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The '{sectionName}' parts are incomplete; set {string.Join(", ", missing)}."
            );
        }

        var builder = new DbConnectionStringBuilder
        {
            ["Host"] = parts.Host,
            ["Database"] = parts.Name,
            ["Username"] = parts.Username,
            ["Password"] = parts.Password,
        };

        if (string.IsNullOrEmpty(parts.Port))
            return builder.ConnectionString;

        if (
            !int.TryParse(
                parts.Port,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var portNumber
            ) || portNumber is < 1 or > 65535
        )
        {
            throw new InvalidOperationException(
                $"The '{sectionName}:{nameof(PostgresConnectionParts.Port)}' value is not a port number from 1 to 65535."
            );
        }

        builder["Port"] = portNumber.ToString(CultureInfo.InvariantCulture);

        return builder.ConnectionString;
    }
}
