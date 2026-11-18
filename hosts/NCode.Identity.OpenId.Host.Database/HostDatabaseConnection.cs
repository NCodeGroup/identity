// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Resolves the reference OP host's Postgres connection string from configuration. The application and its migration
/// worker both resolve it here, so the two forms of one connection are judged in one place.
/// </summary>
/// <remarks>
/// The connection is configured as exactly one of two forms: a finished connection string under
/// <c>ConnectionStrings:openiddb</c> (what Aspire supplies), or the parts under the <c>OpenIdDatabase</c> section
/// (<c>Host</c>, <c>Port</c>, <c>Name</c>, <c>Username</c> and <c>Password</c>, the values a Postgres operator writes
/// to its Secret). <see cref="PostgresConnectionRules"/> judges and assembles the two forms, so a password that holds
/// <c>;</c>, <c>'</c> or <c>"</c> stays a password, and the section binds strictly, so a key that is not one of the
/// parts stops the host.
/// </remarks>
// See ADR-1509. The application is the OpenId host's api project and the migration worker is the MigrationService
// project.
[PublicAPI]
public static class HostDatabaseConnection
{
    /// <summary>
    /// The name of the connection string, <c>ConnectionStrings:openiddb</c>.
    /// </summary>
    public const string ConnectionStringName = "openiddb";

    /// <summary>
    /// The name of the configuration section that holds the connection's parts.
    /// </summary>
    public const string SectionName = "OpenIdDatabase";

    /// <summary>
    /// Resolves the Postgres connection string from <paramref name="configuration"/>.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <returns>The connection string, or <see langword="null"/> when neither form is configured.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Both forms are configured, the section holds a key that is not one of the parts, a required part is missing, or
    /// the port is not a number from 1 to 65535. The message names the configuration paths and never holds a
    /// configured value.
    /// </exception>
    public static string? Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return PostgresConnectionRules.Resolve(configuration, ConnectionStringName, SectionName);
    }
}
