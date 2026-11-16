// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NCode.Identity.OpenId.Persistence.EntityFramework;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Applies the EF Core migrations for every host context against the Postgres database. Invoked by the
/// <c>MigrationService</c> worker so schema application is a dedicated Aspire resource: a migration failure surfaces as
/// a failed dashboard resource instead of crashing the <c>api</c> host.
/// </summary>
// See ADR-1073.
[PublicAPI]
public static class MigrationRunner
{
    /// <summary>
    /// Applies pending migrations for the <see cref="OpenIdDbContext"/> and the
    /// <see cref="DeveloperDataProtectionDbContext"/>, which share one Postgres database. Before it applies any, it
    /// checks that every migration each context's history records is one this assembly holds, and applies nothing when
    /// one is not.
    /// </summary>
    /// <param name="serviceProvider">A provider that has the OpenID persistence slice registered (id generator and
    /// model contributors), used to construct the contexts outside a request scope.</param>
    /// <param name="connectionString">The Postgres connection string supplied by Aspire.</param>
    /// <param name="runtimeRole">The name of the database role the host runs as. After it migrates, the runner enables
    /// row-level security on every tenant-bound table and grants this role its data access, in one transaction.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">A context's migrations history records a migration this assembly
    /// does not hold.</exception>
    /// <exception cref="ArgumentException"><paramref name="runtimeRole"/> is not a valid role name.</exception>
    [ExcludeFromCodeCoverage(
        Justification = "Migrates a live database; RowLevelSecurityEndToEndTests covers it."
    )]
    public static async ValueTask MigrateAllAsync(
        IServiceProvider serviceProvider,
        string connectionString,
        string runtimeRole,
        CancellationToken cancellationToken
    )
    {
        await using var openIdContext = HostDatabase.CreateUnscopedOpenIdContext(
            serviceProvider,
            connectionString
        );
        await using var dataProtectionContext = HostDatabase.CreateDataProtectionContext(
            connectionString
        );

        await ThrowIfUnknownMigrationsAppliedAsync(openIdContext, cancellationToken);
        await ThrowIfUnknownMigrationsAppliedAsync(dataProtectionContext, cancellationToken);

        await openIdContext.Database.MigrateAsync(cancellationToken);
        await dataProtectionContext.Database.MigrateAsync(cancellationToken);

        var statements = CreateAccessStatements(
            openIdContext.GetService<IDesignTimeModel>().Model,
            dataProtectionContext.GetService<IDesignTimeModel>().Model,
            runtimeRole
        );

        await using var transaction = await openIdContext.Database.BeginTransactionAsync(
            cancellationToken
        );
        foreach (var statement in statements)
            await openIdContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the statements that give <paramref name="runtimeRole"/> its access to the host database: the row-level
    /// security and grants of the OpenID model, followed by the grants of the Data Protection model, which has no
    /// tenant-bound table.
    /// </summary>
    /// <param name="openIdModel">The model of the <see cref="OpenIdDbContext"/>.</param>
    /// <param name="dataProtectionModel">The model of the <see cref="DeveloperDataProtectionDbContext"/>.</param>
    /// <param name="runtimeRole">The name of the database role the host runs as.</param>
    /// <returns>The statements to run, in order, each as one command.</returns>
    /// <exception cref="ArgumentException"><paramref name="runtimeRole"/> is not a valid role name.</exception>
    // See ADR-1018.
    internal static IReadOnlyList<string> CreateAccessStatements(
        IModel openIdModel,
        IModel dataProtectionModel,
        string runtimeRole
    )
    {
        var statements = new List<string>(
            RowLevelSecurity.CreateStatements(openIdModel, runtimeRole)
        );
        statements.AddRange(
            RowLevelSecurity.CreateGrantStatements(dataProtectionModel, runtimeRole)
        );
        return statements;
    }

    [ExcludeFromCodeCoverage(
        Justification = "Reads a live database's migrations history; RowLevelSecurityEndToEndTests covers it."
    )]
    private static async ValueTask ThrowIfUnknownMigrationsAppliedAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        ThrowIfUnknownMigrationsApplied(
            context.GetType().Name,
            appliedMigrations,
            context.Database.GetMigrations()
        );
    }

    /// <summary>
    /// Throws when <paramref name="appliedMigrations"/> holds an identifier that <paramref name="migrations"/> does not,
    /// so the migration worker stops with a message that says how to recover instead of a provider error from a
    /// <c>CREATE TABLE</c> over a schema another <c>InitialCreate</c> created.
    /// </summary>
    /// <param name="contextName">The name of the context whose migrations are compared.</param>
    /// <param name="appliedMigrations">The migration identifiers the context's migrations history records.</param>
    /// <param name="migrations">The migration identifiers this assembly holds for the context.</param>
    /// <exception cref="InvalidOperationException"><paramref name="appliedMigrations"/> holds an identifier that
    /// <paramref name="migrations"/> does not.</exception>
    // See ADR-1180.
    internal static void ThrowIfUnknownMigrationsApplied(
        string contextName,
        IEnumerable<string> appliedMigrations,
        IEnumerable<string> migrations
    )
    {
        var unknown = MigrationHistoryRules.FindUnknownMigrations(appliedMigrations, migrations);
        if (unknown.Count == 0)
            return;

        throw new InvalidOperationException(
            $"The database's {contextName} migrations history records {string.Join(", ", unknown)}, which this host "
                + $"does not hold, so its {contextName} migrations cannot be applied to it. Each host database "
                + "context has one InitialCreate, which is re-scaffolded with a fresh identifier on each model "
                + "change. Stop the AppHost and reset the local database with build/dev-db-reset.ps1; the next "
                + "AppHost run creates the schema and seeds the development data."
        );
    }
}
