// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using NCode.Identity.Persistence;
using Npgsql;

namespace NCode.Identity.OpenId.LocalDev.Database;

/// <summary>
/// Creates the Postgres role that the reference OP hosts run as in local development, and names the connection parts
/// that carry its credentials.
/// </summary>
/// <remarks>
/// The migration worker connects as the schema owner and grants the runtime role its data access after it migrates, so
/// the hosts never own the tables that row-level security protects. The role's password is a per-machine development
/// secret in URL-safe Base64, so it embeds in a statement without quoting; the hosts quote it themselves when they
/// assemble their connection.
/// </remarks>
// See ADR-1018, ADR-1073 and ADR-1370.
internal static partial class LocalDatabase
{
    /// <summary>
    /// The name of the runtime role, <c>ncode_identity_app</c>.
    /// </summary>
    internal const string RuntimeRole = "ncode_identity_app";

    /// <summary>
    /// The name of the database that the runtime connection opens, <c>openiddb</c>.
    /// </summary>
    internal const string DatabaseName = "openiddb";

    /// <summary>
    /// The name of the configuration section whose parts the hosts assemble their Postgres connection from.
    /// </summary>
    // See ADR-1509.
    internal const string ConnectionPartsSection = "OpenIdDatabase";

    /// <summary>
    /// Creates the statement that makes the runtime role exist with its login attributes and password.
    /// </summary>
    /// <remarks>
    /// The statement is one <c>DO</c> block, so it is safe on every start: it creates the role when
    /// <c>pg_roles</c> has none, and otherwise alters the role to the same attributes and password.
    /// </remarks>
    /// <param name="runtimeRole">
    /// The role name; it is a valid name according to <see cref="DatabaseRoleNameRules"/>.
    /// </param>
    /// <param name="password">The role's password; it is URL-safe Base64 (<c>A-Z a-z 0-9 - _</c>).</param>
    /// <returns>The statement.</returns>
    /// <exception cref="ArgumentException">The role or the password is not valid.</exception>
    internal static string RoleScript(string runtimeRole, string password)
    {
        ThrowIfInvalid(runtimeRole, password);

        return $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{runtimeRole}') THEN
                    CREATE ROLE "{runtimeRole}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{password}';
                ELSE
                    ALTER ROLE "{runtimeRole}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{password}';
                END IF;
            END
            $$
            """;
    }

    /// <summary>
    /// Gets the connection parts, as environment variables, that name the dev database, the runtime role and its
    /// password.
    /// </summary>
    /// <param name="password">The role's password; it is URL-safe Base64 (<c>A-Z a-z 0-9 - _</c>).</param>
    /// <returns>The value of each environment variable, keyed by its name.</returns>
    /// <exception cref="ArgumentException">The password is not valid.</exception>
    internal static IReadOnlyDictionary<string, string> RuntimeDatabaseParts(string password)
    {
        ThrowIfInvalid(RuntimeRole, password);

        return new Dictionary<string, string>(3, StringComparer.Ordinal)
        {
            [$"{ConnectionPartsSection}__Name"] = DatabaseName,
            [$"{ConnectionPartsSection}__Username"] = RuntimeRole,
            [$"{ConnectionPartsSection}__Password"] = password,
        };
    }

    /// <summary>
    /// Creates or updates the runtime role on the Postgres server.
    /// </summary>
    /// <param name="serverConnectionString">The connection string of the server, without a database.</param>
    /// <param name="password">The role's password; it is URL-safe Base64 (<c>A-Z a-z 0-9 - _</c>).</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A value task that completes when the role exists.</returns>
    /// <exception cref="ArgumentException">The password is not valid.</exception>
    internal static async ValueTask EnsureRoleAsync(
        string serverConnectionString,
        string password,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverConnectionString);

        var script = RoleScript(RuntimeRole, password);
        var builder = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            Database = "postgres",
        };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(script, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Checks that a role name and a password are safe to embed in a statement.
    /// </summary>
    /// <param name="runtimeRole">
    /// The role name; it is a valid name according to <see cref="DatabaseRoleNameRules"/>.
    /// </param>
    /// <param name="password">The role's password; it is URL-safe Base64 (<c>A-Z a-z 0-9 - _</c>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="runtimeRole"/> or <paramref name="password"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The role or the password does not match its pattern.</exception>
    private static void ThrowIfInvalid(string runtimeRole, string password)
    {
        ArgumentNullException.ThrowIfNull(runtimeRole);
        ArgumentNullException.ThrowIfNull(password);

        if (!DatabaseRoleNameRules.IsValid(runtimeRole))
        {
            throw new ArgumentException(
                $"The runtime role must match {DatabaseRoleNameRules.Pattern}.",
                nameof(runtimeRole)
            );
        }

        if (!PasswordPattern().IsMatch(password))
        {
            throw new ArgumentException(
                "The password must be URL-safe Base64 (A-Z a-z 0-9 - _).",
                nameof(password)
            );
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordPattern();

    extension(IResourceBuilder<PostgresServerResource> postgres)
    {
        /// <summary>
        /// Creates or updates the runtime role when the Postgres server is ready.
        /// </summary>
        /// <param name="password">The role's password; it is URL-safe Base64 (<c>A-Z a-z 0-9 - _</c>).</param>
        /// <returns>The same resource builder, to chain further calls.</returns>
        internal IResourceBuilder<PostgresServerResource> WithRuntimeRole(string password)
        {
            ArgumentNullException.ThrowIfNull(postgres);
            ArgumentNullException.ThrowIfNull(password);

            postgres.ApplicationBuilder.Eventing.Subscribe<ResourceReadyEvent>(
                postgres.Resource,
                async (_, cancellationToken) =>
                {
                    var serverConnectionString =
                        await postgres.Resource.ConnectionStringExpression.GetValueAsync(
                            cancellationToken
                        )
                        ?? throw new InvalidOperationException(
                            "The Postgres server has no connection string."
                        );

                    await EnsureRoleAsync(serverConnectionString, password, cancellationToken);
                }
            );

            return postgres;
        }
    }
}
