// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace NCode.Identity.OpenId.LocalDev.Database;

/// <summary>
/// Creates or updates the runtime role on the dev database's Postgres server once the server is ready.
/// </summary>
[ExcludeFromCodeCoverage(
    Justification = "Aspire resource wiring that runs the role statement on a live Postgres server."
)]
internal static class RuntimeRoleRegistration
{
    /// <summary>
    /// Runs the role statement on the Postgres server's maintenance database.
    /// </summary>
    /// <param name="server">The Postgres server, which is ready.</param>
    /// <param name="script">The statement that <see cref="LocalDatabase.RoleScript"/> creates.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A value task that completes when the role exists.</returns>
    /// <exception cref="InvalidOperationException">The Postgres server has no connection string.</exception>
    private static async ValueTask EnsureRoleAsync(
        PostgresServerResource server,
        string script,
        CancellationToken cancellationToken
    )
    {
        var serverConnectionString =
            await server.ConnectionStringExpression.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException("The Postgres server has no connection string.");

        var maintenanceConnectionString = LocalDatabase.MaintenanceConnectionString(
            serverConnectionString
        );
        await using var connection = new NpgsqlConnection(maintenanceConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(script, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    extension(IResourceBuilder<PostgresServerResource> postgres)
    {
        /// <summary>
        /// Creates or updates the runtime role when the Postgres server is ready.
        /// </summary>
        /// <param name="password">The role's password; it is URL-safe Base64 (<c>A-Z a-z 0-9 - _</c>).</param>
        /// <returns>The same resource builder, to chain further calls.</returns>
        /// <exception cref="ArgumentException">The password is not valid.</exception>
        internal IResourceBuilder<PostgresServerResource> WithRuntimeRole(string password)
        {
            ArgumentNullException.ThrowIfNull(postgres);
            ArgumentNullException.ThrowIfNull(password);

            var script = LocalDatabase.RoleScript(LocalDatabase.RuntimeRole, password);

            return postgres.OnResourceReady(
                (server, _, cancellationToken) =>
                    EnsureRoleAsync(server, script, cancellationToken).AsTask()
            );
        }
    }
}
