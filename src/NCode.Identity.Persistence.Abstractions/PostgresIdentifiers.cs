// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Persistence;

/// <summary>
/// Contains the quoting rule for the identifiers in the Postgres statements that the hosts generate, so every name
/// reaches the server as written, a double quote included.
/// </summary>
[PublicAPI]
public static class PostgresIdentifiers
{
    /// <summary>
    /// Quotes an identifier, doubling every double quote it holds.
    /// </summary>
    /// <param name="identifier">The name of a table, column, sequence, schema or role.</param>
    /// <returns>The identifier in double quotes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="identifier"/> is empty, which Postgres refuses as a quoted identifier.
    /// </exception>
    public static string Quote(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// Quotes the name of a table or sequence for use in a statement, qualified by its schema when it has one.
    /// </summary>
    /// <param name="schema">The schema, or <c>null</c> for the default schema.</param>
    /// <param name="name">The name of the table or sequence.</param>
    /// <returns>The quoted name, as <c>"schema"."name"</c> or <c>"name"</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="schema"/> or <paramref name="name"/> is empty.
    /// </exception>
    public static string QualifiedName(string? schema, string name) =>
        schema is null ? Quote(name) : $"{Quote(schema)}.{Quote(name)}";
}
