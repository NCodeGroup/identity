// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NCode.Identity.OpenId.Persistence.EntityFramework.Entities;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Generates the Postgres row-level security and grant statements for the host database from the EF Core model, so the
/// database enforces the tenant scope that the global query filter applies in the application.
/// </summary>
/// <remarks>
/// Every table of an <see cref="ISupportTenantEntity"/> gets <c>ENABLE</c> and <c>FORCE ROW LEVEL SECURITY</c> and one
/// <c>tenant_isolation</c> policy that admits a row when the session is unscoped or the row's tenant column equals the
/// session's tenant. The statements read each table's schema, name and tenant column from the model and are ordered by
/// table name, so the same model always yields the same statements. They are idempotent: a policy is dropped before it
/// is created.
/// </remarks>
// See ADR-1018.
[PublicAPI]
public static class RowLevelSecurity
{
    /// <summary>
    /// The name of the policy that scopes every tenant-bound table.
    /// </summary>
    public const string PolicyName = "tenant_isolation";

    /// <summary>
    /// Creates the statements that enable row-level security on every tenant-bound table of <paramref name="model"/> and
    /// grant the data access that <paramref name="runtimeRole"/> needs: <c>SELECT</c>, <c>INSERT</c>, <c>UPDATE</c> and
    /// <c>DELETE</c> on every table, and <c>USAGE</c> on every sequence.
    /// </summary>
    /// <param name="model">The model of the host's <c>OpenIdDbContext</c>.</param>
    /// <param name="runtimeRole">
    /// The name of the database role the host runs as. It is a valid name according to <see cref="DatabaseRoleNameRules"/>, so it is safe to quote.
    /// </param>
    /// <returns>The statements to run, in order, each as one command.</returns>
    /// <exception cref="ArgumentException"><paramref name="runtimeRole"/> is not a valid role name.</exception>
    public static IReadOnlyList<string> CreateStatements(IModel model, string runtimeRole)
    {
        ValidateArguments(model, runtimeRole);

        var statements = new List<string>();
        foreach (var table in FindTables(model))
        {
            if (table.TenantColumn is not { } tenantColumn)
                continue;

            var name = QualifiedName(table.Schema, table.Name);
            var expression =
                $"current_setting('{TenantSessionSettings.Unscoped}', true) = '{TenantSessionSettings.On}' "
                + $"OR {Quote(tenantColumn)} = current_setting('{TenantSessionSettings.TenantId}', true)";

            statements.Add($"ALTER TABLE {name} ENABLE ROW LEVEL SECURITY");
            statements.Add($"ALTER TABLE {name} FORCE ROW LEVEL SECURITY");
            statements.Add($"DROP POLICY IF EXISTS {PolicyName} ON {name}");
            statements.Add(
                $"CREATE POLICY {PolicyName} ON {name} USING ({expression}) WITH CHECK ({expression})"
            );
        }

        statements.AddRange(CreateGrantStatements(model, runtimeRole));
        return statements;
    }

    /// <summary>
    /// Creates the statements that grant <paramref name="runtimeRole"/> the data access it needs on <paramref name="model"/>:
    /// <c>SELECT</c>, <c>INSERT</c>, <c>UPDATE</c> and <c>DELETE</c> on every table, and <c>USAGE</c> on every
    /// sequence. The statements enable no row-level security.
    /// </summary>
    /// <param name="model">The model of a host context.</param>
    /// <param name="runtimeRole">
    /// The name of the database role the host runs as. It is a valid name according to <see cref="DatabaseRoleNameRules"/>, so it is safe to quote.
    /// </param>
    /// <returns>The statements to run, in order, each as one command.</returns>
    /// <exception cref="ArgumentException"><paramref name="runtimeRole"/> is not a valid role name.</exception>
    public static IReadOnlyList<string> CreateGrantStatements(IModel model, string runtimeRole)
    {
        ValidateArguments(model, runtimeRole);

        var role = Quote(runtimeRole);

        var statements = new List<string>();
        foreach (var table in FindTables(model))
            statements.Add(
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON {QualifiedName(table.Schema, table.Name)} TO {role}"
            );

        var sequences = model
            .GetSequences()
            .OrderBy(sequence => sequence.Schema, StringComparer.Ordinal)
            .ThenBy(sequence => sequence.Name, StringComparer.Ordinal);
        foreach (var sequence in sequences)
            statements.Add(
                $"GRANT USAGE ON SEQUENCE {QualifiedName(sequence.Schema, sequence.Name)} TO {role}"
            );

        return statements;
    }

    /// <summary>
    /// Checks the arguments that every statement factory takes.
    /// </summary>
    /// <param name="model">The model that the statements are generated from.</param>
    /// <param name="runtimeRole">The name of the database role the host runs as.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="runtimeRole"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="runtimeRole"/> is not a valid role name.</exception>
    private static void ValidateArguments(IModel model, string runtimeRole)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(runtimeRole);

        if (!DatabaseRoleNameRules.IsValid(runtimeRole))
            throw new ArgumentException(
                $"The runtime role '{runtimeRole}' must match {DatabaseRoleNameRules.Pattern}.",
                nameof(runtimeRole)
            );
    }

    /// <summary>
    /// Finds every table that the model maps, with the column that holds its tenant. The tables are ordered by schema
    /// and then by name, both ordinally, so the generated statements are stable. Several entity types that share a
    /// table yield one entry.
    /// </summary>
    /// <param name="model">The model to read the tables from.</param>
    /// <returns>
    /// The schema, the name and the tenant column of each table. The tenant column is <c>null</c> when no entity type
    /// of the table is an <see cref="ISupportTenantEntity"/>, and then the table is not tenant-bound.
    /// </returns>
    private static List<(string? Schema, string Name, string? TenantColumn)> FindTables(
        IModel model
    )
    {
        var tables = new SortedDictionary<
            (string Schema, string Name),
            (string? Schema, string Name, string? TenantColumn)
        >(
            Comparer<(string Schema, string Name)>.Create(
                (x, y) =>
                {
                    var bySchema = string.CompareOrdinal(x.Schema, y.Schema);
                    return bySchema != 0 ? bySchema : string.CompareOrdinal(x.Name, y.Name);
                }
            )
        );

        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() is not { } tableName)
                continue;

            var schema = entityType.GetSchema();
            var key = (schema ?? string.Empty, tableName);
            tables.TryGetValue(key, out var table);

            var tenantColumn = table.TenantColumn;
            if (
                tenantColumn is null
                && typeof(ISupportTenantEntity).IsAssignableFrom(entityType.ClrType)
            )
                tenantColumn = FindTenantColumn(entityType, tableName, schema);

            tables[key] = (schema, tableName, tenantColumn);
        }

        return [.. tables.Values];
    }

    /// <summary>
    /// Finds the name of the column that stores the normalized tenant identifier of a tenant-bound entity type in a
    /// table.
    /// </summary>
    /// <param name="entityType">The entity type that implements <see cref="ISupportTenantEntity"/>.</param>
    /// <param name="tableName">The name of the table that the entity type maps to.</param>
    /// <param name="schema">The schema of the table, or <c>null</c> for the default schema.</param>
    /// <returns>The column name of the <see cref="ISupportTenantEntity.NormalizedTenantId"/> property.</returns>
    /// <exception cref="InvalidOperationException">
    /// The entity type has no mapped tenant property, or the property has no column in the table.
    /// </exception>
    private static string FindTenantColumn(IEntityType entityType, string tableName, string? schema)
    {
        var property =
            entityType.FindProperty(nameof(ISupportTenantEntity.NormalizedTenantId))
            ?? throw new InvalidOperationException(
                $"The entity type '{entityType.DisplayName()}' has no mapped "
                    + $"{nameof(ISupportTenantEntity.NormalizedTenantId)} property."
            );

        return property.GetColumnName(StoreObjectIdentifier.Table(tableName, schema))
            ?? throw new InvalidOperationException(
                $"The {nameof(ISupportTenantEntity.NormalizedTenantId)} property of '{entityType.DisplayName()}' "
                    + $"has no column in table '{tableName}'."
            );
    }

    /// <summary>
    /// Quotes the name of a table or sequence for use in a statement, qualified by its schema when it has one.
    /// </summary>
    /// <param name="schema">The schema, or <c>null</c> or empty for the default schema.</param>
    /// <param name="name">The name of the table or sequence.</param>
    /// <returns>The quoted name, as <c>"schema"."name"</c> or <c>"name"</c>.</returns>
    private static string QualifiedName(string? schema, string name) =>
        string.IsNullOrEmpty(schema) ? Quote(name) : $"{Quote(schema)}.{Quote(name)}";

    /// <summary>
    /// Quotes an identifier for use in a statement, doubling every embedded double quote.
    /// </summary>
    /// <param name="identifier">The name of a table, column, sequence, schema or role.</param>
    /// <returns>The identifier in double quotes.</returns>
    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
