// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Persistence;

/// <summary>
/// Contains the rule that compares a database's migrations history with the migrations an assembly holds. A database
/// whose migrations history records an identifier the assembly does not hold was created by another
/// <c>InitialCreate</c>, so applying the assembly's migrations to it fails; each runner refuses it with its own
/// recovery.
/// </summary>
// See ADR-1180.
[PublicAPI]
public static class MigrationHistoryRules
{
    /// <summary>
    /// Finds the applied migrations that the assembly does not hold, compared ordinally.
    /// </summary>
    /// <param name="appliedMigrations">The migration identifiers that the database's migrations history
    /// records.</param>
    /// <param name="migrations">The migration identifiers that the assembly holds.</param>
    /// <returns>
    /// The applied identifiers that <paramref name="migrations"/> does not hold, in history order; empty when the
    /// assembly holds every applied migration.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="appliedMigrations"/> or <paramref name="migrations"/> is <see langword="null"/>.
    /// </exception>
    public static IReadOnlyList<string> FindUnknownMigrations(
        IEnumerable<string> appliedMigrations,
        IEnumerable<string> migrations
    )
    {
        ArgumentNullException.ThrowIfNull(appliedMigrations);
        ArgumentNullException.ThrowIfNull(migrations);

        var known = migrations.ToHashSet(StringComparer.Ordinal);
        return [.. appliedMigrations.Where(id => !known.Contains(id))];
    }
}
