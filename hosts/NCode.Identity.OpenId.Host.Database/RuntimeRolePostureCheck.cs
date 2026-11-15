// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NCode.Identity.OpenId.Host.Database.Logging;
using Npgsql;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Checks, when the host starts and before it accepts traffic, that the Postgres role the host runs as is subject to
/// row-level security (see <see cref="RuntimeRolePosture"/>). Under the deployment posture a violation stops the
/// host; under <c>LocalDevelopment:Enabled</c> it logs one warning and the host starts.
/// </summary>
/// <param name="connectionString">The connection string the host's runtime role connects with.</param>
/// <param name="localDevelopment">
/// Whether the host runs as a local inner-loop host (<c>LocalDevelopment:Enabled</c>), which warns instead of
/// refusing to start.
/// </param>
/// <param name="logger">The logger that receives the warning.</param>
// See ADR-1370.
public sealed class RuntimeRolePostureCheck(
    string connectionString,
    bool localDevelopment,
    ILogger<RuntimeRolePostureCheck> logger
) : IHostedService
{
    private const string PostureQuery = """
        SELECT
            r.rolsuper,
            r.rolbypassrls,
            EXISTS (
                SELECT 1
                FROM pg_tables t
                WHERE t.tableowner = current_user AND t.schemaname = current_schema()
            )
        FROM pg_roles r
        WHERE r.rolname = current_user
        """;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var violations = await ReadViolationsAsync(cancellationToken);
        if (violations.Count == 0)
            return;

        var joined = string.Join("; ", violations);

        if (localDevelopment)
        {
            logger.RuntimeRolePostureViolated(joined, RuntimeRolePosture.Remedy);
            return;
        }

        throw new InvalidOperationException(
            $"The runtime database role is not subject to row-level security: {joined}. {RuntimeRolePosture.Remedy}"
        );
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Reads the posture of the role that the host runs as and judges it.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// The reasons the role is not subject to row-level security, which is empty when the role is subject to it.
    /// </returns>
    /// <exception cref="InvalidOperationException">The database has no row for the connected role.</exception>
    private async ValueTask<IReadOnlyList<string>> ReadViolationsAsync(
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(PostureQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The runtime database role was not found in pg_roles, so its row-level security posture cannot be judged."
            );
        }

        return RuntimeRolePosture.Evaluate(
            isSuperuser: reader.GetBoolean(0),
            bypassesRls: reader.GetBoolean(1),
            ownsTenantTables: reader.GetBoolean(2)
        );
    }
}
