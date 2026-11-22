// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace NCode.Identity.OpenId.Host.Database.Facades;

/// <summary>
/// Reads the posture of the connected role from <c>pg_roles</c> and <c>pg_tables</c> over Npgsql.
/// </summary>
/// <param name="connectionString">The connection string the host's runtime role connects with.</param>
[ExcludeFromCodeCoverage(
    Justification = "Reads pg_roles from a live database; RowLevelSecurityEndToEndTests covers it."
)]
internal sealed class NpgsqlRuntimeRolePostureReader(string connectionString)
    : IRuntimeRolePostureReader
{
    /// <summary>
    /// The query that reads the connected role's posture.
    /// </summary>
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
    public async ValueTask<RuntimeRolePostureRow?> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(PostureQuery, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var isSuperuser = reader.GetBoolean(0);
        var bypassesRls = reader.GetBoolean(1);
        var ownsTenantTables = reader.GetBoolean(2);

        return new RuntimeRolePostureRow(
            IsSuperuser: isSuperuser,
            BypassesRls: bypassesRls,
            OwnsTenantTables: ownsTenantTables
        );
    }
}
