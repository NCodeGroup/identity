// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Persistence;

/// <summary>
/// Creates the Postgres statements that grant a runtime role the data access it needs: <c>USAGE</c> on every schema
/// that holds one of its tables or sequences, <c>SELECT</c>, <c>INSERT</c>, <c>UPDATE</c> and <c>DELETE</c> on every
/// table, and <c>USAGE</c> on every sequence.
/// </summary>
/// <remarks>
/// The role that migrates a database owns its tables, and the replicas connect as the runtime role, which owns nothing
/// and can only read and write rows. The statements come in a stable order: the schemas, then the tables, then the
/// sequences, each ordered ordinally by schema and then by name, with the default schema first.
/// </remarks>
// See ADR-1018 and ADR-1073.
[PublicAPI]
public static class RuntimeRoleGrants
{
    /// <summary>
    /// Creates the statements that grant <paramref name="runtimeRole"/> the data access it needs on
    /// <paramref name="tables"/> and <paramref name="sequences"/>.
    /// </summary>
    /// <param name="runtimeRole">
    /// The name of the database role the replicas connect as. It is a valid name according to
    /// <see cref="DatabaseRoleNameRules"/>.
    /// </param>
    /// <param name="tables">The tables to grant, in any order; a table named twice is granted once.</param>
    /// <param name="sequences">The sequences to grant, in any order; a sequence named twice is granted once.</param>
    /// <returns>The statements to run, in order, each as one command.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="runtimeRole"/>, <paramref name="tables"/> or <paramref name="sequences"/> is <c>null</c>, or a
    /// table or sequence has no name.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="runtimeRole"/> is not a valid role name according to <see cref="DatabaseRoleNameRules"/>, or a
    /// table or sequence has an empty name or schema.
    /// </exception>
    public static IReadOnlyList<string> CreateStatements(
        string runtimeRole,
        IEnumerable<DatabaseObjectName> tables,
        IEnumerable<DatabaseObjectName> sequences
    )
    {
        ArgumentNullException.ThrowIfNull(runtimeRole);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(sequences);

        if (!DatabaseRoleNameRules.IsValid(runtimeRole))
            throw new ArgumentException(
                $"The runtime role '{runtimeRole}' must match {DatabaseRoleNameRules.Pattern}.",
                nameof(runtimeRole)
            );

        var role = PostgresIdentifiers.Quote(runtimeRole);

        var order = Comparer<DatabaseObjectName>.Create(
            (x, y) =>
            {
                var bySchema = string.CompareOrdinal(x.Schema, y.Schema);
                return bySchema != 0 ? bySchema : string.CompareOrdinal(x.Name, y.Name);
            }
        );
        var tablesInOrder = new SortedSet<DatabaseObjectName>(tables, order);
        var sequencesInOrder = new SortedSet<DatabaseObjectName>(sequences, order);

        var schemas = tablesInOrder
            .Concat(sequencesInOrder)
            .Select(objectName => objectName.Schema)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        var statements = new List<string>();
        foreach (var schema in schemas)
            statements.Add($"GRANT USAGE ON SCHEMA {PostgresIdentifiers.Quote(schema)} TO {role}");

        foreach (var table in tablesInOrder)
        {
            var name = PostgresIdentifiers.QualifiedName(table.Schema, table.Name);
            statements.Add($"GRANT SELECT, INSERT, UPDATE, DELETE ON {name} TO {role}");
        }

        foreach (var sequence in sequencesInOrder)
        {
            var name = PostgresIdentifiers.QualifiedName(sequence.Schema, sequence.Name);
            statements.Add($"GRANT USAGE ON SEQUENCE {name} TO {role}");
        }

        return statements;
    }
}
