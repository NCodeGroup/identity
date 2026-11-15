// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Npgsql;

namespace NCode.Identity.OpenId.LocalDev.Database;

/// <summary>
/// Creates the Postgres role that the reference OP hosts run as in local development, and builds the connection string
/// that carries its credentials.
/// </summary>
/// <remarks>
/// The migration worker connects as the schema owner and grants the runtime role its data access after it migrates, so
/// the hosts never own the tables that row-level security protects. The role's password is a per-machine development
/// secret in URL-safe Base64, so it embeds in a statement and in a connection string without quoting.
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
    /// Creates the statement that makes the runtime role exist with its login attributes and password.
    /// </summary>
    /// <remarks>
    /// The statement is one <c>DO</c> block, so it is safe on every start: it creates the role when
    /// <c>pg_roles</c> has none, and otherwise alters the role to the same attributes and password.
    /// </remarks>
    /// <param name="runtimeRole">The role name; it matches <c>^[a-z_][a-z0-9_]*$</c>.</param>
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
    /// Creates the connection string that the hosts use to open the dev database as the runtime role.
    /// </summary>
    /// <param name="endpoint">The endpoint of the Postgres server.</param>
    /// <param name="password">The role's password; it is URL-safe Base64 (<c>A-Z a-z 0-9 - _</c>).</param>
    /// <returns>The connection string, resolved once the endpoint is allocated.</returns>
    /// <exception cref="ArgumentException">The password is not valid.</exception>
    internal static ReferenceExpression RuntimeConnectionString(
        EndpointReference endpoint,
        string password
    )
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ThrowIfInvalid(RuntimeRole, password);

        return ReferenceExpression.Create(
            $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Database={DatabaseName};Username={RuntimeRole};Password={password}"
        );
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

    private static void ThrowIfInvalid(string runtimeRole, string password)
    {
        ArgumentNullException.ThrowIfNull(runtimeRole);
        ArgumentNullException.ThrowIfNull(password);

        if (!RuntimeRolePattern().IsMatch(runtimeRole))
        {
            throw new ArgumentException(
                "The runtime role must match ^[a-z_][a-z0-9_]*$.",
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

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeRolePattern();

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
