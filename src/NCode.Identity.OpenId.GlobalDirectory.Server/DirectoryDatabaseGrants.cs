// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Creates the statements that grant the directory's runtime role the data access it needs on the
/// <see cref="DirectoryDbContext"/> model. The role that migrates the database owns the tables, and the directory's
/// replicas connect as the runtime role, which owns nothing and can only read and write rows.
/// </summary>
[PublicAPI]
public static class DirectoryDatabaseGrants
{
    /// <summary>
    /// Creates the statements that grant <paramref name="runtimeRole"/> the data access it needs on
    /// <paramref name="model"/>: <c>USAGE</c> on every schema the model names, <c>SELECT</c>, <c>INSERT</c>,
    /// <c>UPDATE</c> and <c>DELETE</c> on every table, and <c>USAGE</c> on every sequence. Every identifier is quoted,
    /// and the statements come in a stable order.
    /// </summary>
    /// <param name="model">The design-time model of the <see cref="DirectoryDbContext"/>.</param>
    /// <param name="runtimeRole">The name of the database role the directory's replicas connect as.</param>
    /// <returns>The statements to run, in order, each as one command.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="model"/> or <paramref name="runtimeRole"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="runtimeRole"/> is not a valid role name according to <see cref="DatabaseRoleNameRules"/>.
    /// </exception>
    public static IReadOnlyList<string> CreateStatements(IModel model, string runtimeRole)
    {
        ArgumentNullException.ThrowIfNull(model);

        var tables = new List<DatabaseObjectName>();
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() is { } tableName)
                tables.Add(new DatabaseObjectName(entityType.GetSchema(), tableName));
        }

        var sequences = model
            .GetSequences()
            .Select(sequence => new DatabaseObjectName(sequence.Schema, sequence.Name));
        return RuntimeRoleGrants.CreateStatements(runtimeRole, tables, sequences);
    }
}
