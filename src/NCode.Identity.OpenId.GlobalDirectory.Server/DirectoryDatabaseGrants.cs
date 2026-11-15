// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Creates the statements that grant the directory's runtime role the data access it needs on the
/// <see cref="DirectoryDbContext"/> model. The role that migrates the database owns the tables, and the directory's
/// replicas connect as the runtime role, which owns nothing and can only read and write rows.
/// </summary>
[PublicAPI]
public static partial class DirectoryDatabaseGrants
{
    /// <summary>
    /// Determines whether <paramref name="role"/> is a valid runtime role name: it matches <c>^[a-z_][a-z0-9_]*$</c>,
    /// so it is safe to quote as an identifier in a statement.
    /// </summary>
    /// <param name="role">The role name to judge.</param>
    /// <returns><see langword="true"/> when the name is valid; otherwise, <see langword="false"/>.</returns>
    public static bool IsValidRole(string role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return RolePattern().IsMatch(role);
    }

    /// <summary>
    /// Creates the statements that grant <paramref name="runtimeRole"/> the data access it needs on
    /// <paramref name="model"/>: <c>USAGE</c> on every schema the model names, <c>SELECT</c>, <c>INSERT</c>,
    /// <c>UPDATE</c> and <c>DELETE</c> on every table, and <c>USAGE</c> on every sequence. Every identifier is quoted,
    /// and the statements come in a stable order.
    /// </summary>
    /// <param name="model">The design-time model of the <see cref="DirectoryDbContext"/>.</param>
    /// <param name="runtimeRole">The name of the database role the directory's replicas connect as.</param>
    /// <returns>The statements to run, in order, each as one command.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="runtimeRole"/> does not match <c>^[a-z_][a-z0-9_]*$</c>.
    /// </exception>
    public static IReadOnlyList<string> CreateStatements(IModel model, string runtimeRole)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(runtimeRole);

        if (!IsValidRole(runtimeRole))
            throw new ArgumentException(
                $"The runtime role '{runtimeRole}' must match ^[a-z_][a-z0-9_]*$.",
                nameof(runtimeRole)
            );

        var role = Quote(runtimeRole);

        var tables = new SortedSet<(string? Schema, string Name)>(
            Comparer<(string? Schema, string Name)>.Create(
                (x, y) =>
                {
                    var bySchema = string.CompareOrdinal(x.Schema, y.Schema);
                    return bySchema != 0 ? bySchema : string.CompareOrdinal(x.Name, y.Name);
                }
            )
        );
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() is { } tableName)
                tables.Add((entityType.GetSchema(), tableName));
        }

        var sequences = model
            .GetSequences()
            .OrderBy(sequence => sequence.Schema, StringComparer.Ordinal)
            .ThenBy(sequence => sequence.Name, StringComparer.Ordinal)
            .ToList();

        var schemas = tables
            .Select(table => table.Schema)
            .Concat(sequences.Select(sequence => sequence.Schema))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        var statements = new List<string>();
        foreach (var schema in schemas)
            statements.Add($"GRANT USAGE ON SCHEMA {Quote(schema)} TO {role}");

        foreach (var (schema, name) in tables)
            statements.Add(
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON {QualifiedName(schema, name)} TO {role}"
            );

        foreach (var sequence in sequences)
            statements.Add(
                $"GRANT USAGE ON SEQUENCE {QualifiedName(sequence.Schema, sequence.Name)} TO {role}"
            );

        return statements;
    }

    private static string QualifiedName(string? schema, string name) =>
        schema is null ? Quote(name) : $"{Quote(schema)}.{Quote(name)}";

    private static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RolePattern();
}
