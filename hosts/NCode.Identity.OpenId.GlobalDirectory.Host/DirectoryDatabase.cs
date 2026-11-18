// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NCode.Identity.OpenId.GlobalDirectory.Server;
using NCode.Identity.Persistence;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.GlobalDirectory.Host;

/// <summary>
/// Npgsql wiring for the directory's own database. The host and the EF Core design-time factory configure the database
/// through these helpers, so the provider and the migrations assembly have one source of truth.
/// </summary>
/// <remarks>
/// The directory's replicas and its <c>migrate</c> command both read the one connection, the <c>directory</c>
/// connection string or the <c>DirectoryDatabase</c> parts, and each workload is given its own role's credentials: the
/// replicas the runtime role's, which owns nothing, and the command the schema owner's. The command applies the
/// migrations and grants the role that <c>DirectoryMigration:RuntimeRole</c> names the data access it needs.
/// </remarks>
internal static class DirectoryDatabase
{
    /// <summary>
    /// The name of the connection string that the replicas and the <c>migrate</c> command read, <c>directory</c>.
    /// </summary>
    public const string ConnectionStringName = "directory";

    /// <summary>
    /// The name of the configuration section that holds the connection's parts, <c>DirectoryDatabase</c>.
    /// </summary>
    public const string SectionName = "DirectoryDatabase";

    /// <summary>
    /// The name of the configuration section that holds the runtime role the <c>migrate</c> command grants data access
    /// to, <c>DirectoryMigration</c>.
    /// </summary>
    public const string MigrationSectionName = "DirectoryMigration";

    /// <summary>
    /// The assembly that owns the EF Core migrations for the <see cref="DirectoryDbContext"/>. The directory server
    /// package ships no migrations.
    /// </summary>
    private static string MigrationsAssembly { get; } =
        typeof(DirectoryDatabase).Assembly.GetName().Name
        ?? throw new InvalidOperationException("The directory host assembly has no name.");

    /// <summary>
    /// Reads the connection string the host runs with, from the <c>directory</c> connection string or the
    /// <c>DirectoryDatabase</c> parts, exactly one of which is configured.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <returns>The connection string.</returns>
    /// <exception cref="InvalidOperationException">
    /// Neither form is configured, both are, the section holds a key that is not one of the parts, a required part is
    /// missing, or the port is not a number from 1 to 65535. The message names the configuration paths and never holds
    /// a configured value.
    /// </exception>
    public static string GetConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return PostgresConnectionRules.Resolve(configuration, ConnectionStringName, SectionName)
            ?? throw new InvalidOperationException(
                "The directory's database is not configured; set the connection string "
                    + "'ConnectionStrings:directory' or the 'DirectoryDatabase' parts."
            );
    }

    /// <summary>
    /// Reads the runtime role the <c>migrate</c> command grants data access to.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <returns>The trimmed role name.</returns>
    /// <exception cref="InvalidOperationException">
    /// The section holds a key that is not one of the <see cref="DirectoryMigrationSettings"/> properties, or the role
    /// is missing, empty or not a valid role name. The message names the key and never holds the value.
    /// </exception>
    public static string GetRuntimeRole(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = new DirectoryMigrationSettings();
        configuration.GetSection(MigrationSectionName).BindStrictly(settings);

        var key = $"{MigrationSectionName}:{nameof(DirectoryMigrationSettings.RuntimeRole)}";

        return DatabaseRoleNameRules.RequireRuntimeRole(
            settings.RuntimeRole,
            key,
            "the directory's replicas connect as"
        );
    }

    /// <summary>
    /// Throws when <paramref name="appliedMigrations"/> holds an identifier that <paramref name="migrations"/> does
    /// not, so the <c>migrate</c> command stops with a message that says how to recover instead of a provider error
    /// from a <c>CREATE TABLE</c> over a schema another <c>InitialCreate</c> created.
    /// </summary>
    /// <param name="appliedMigrations">The migration identifiers the database's migrations history records.</param>
    /// <param name="migrations">The migration identifiers this host holds.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="appliedMigrations"/> holds an identifier that <paramref name="migrations"/> does not.
    /// </exception>
    // See ADR-1180.
    internal static void ThrowIfUnknownMigrationsApplied(
        IEnumerable<string> appliedMigrations,
        IEnumerable<string> migrations
    )
    {
        var unknown = MigrationHistoryRules.FindUnknownMigrations(appliedMigrations, migrations);
        if (unknown.Count == 0)
            return;

        var unknownIdentifiers = string.Join(", ", unknown);

        throw new InvalidOperationException(
            $"The directory database's DirectoryDbContext migrations history records {unknownIdentifiers}, "
                + "which this host does not hold, so its migrations cannot be applied to it. The directory's "
                + "DbContext has one InitialCreate, which is re-scaffolded with a fresh identifier on each model "
                + "change. Drop and recreate the directory's database, then run the migrate command again."
        );
    }

    /// <summary>
    /// Checks the database's migrations history, applies the directory's migrations, then grants
    /// <paramref name="runtimeRole"/> the data access it needs on every table and sequence, in one transaction, so a
    /// failed grant leaves no half-granted schema.
    /// </summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="runtimeRole">The runtime role to grant data access to.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the database is migrated and granted.</returns>
    /// <exception cref="InvalidOperationException">
    /// The database's migrations history records a migration this host does not hold. Nothing is applied.
    /// </exception>
    [ExcludeFromCodeCoverage(
        Justification = "Migrates a live database; DirectoryMigrateCommandEndToEndTests covers it."
    )]
    public static async ValueTask MigrateAsync(
        IServiceProvider services,
        string runtimeRole,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        var dbContextFactory = services.GetRequiredService<IDbContextFactory<DirectoryDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync(
            cancellationToken
        );
        var knownMigrations = dbContext.Database.GetMigrations();
        ThrowIfUnknownMigrationsApplied(appliedMigrations, knownMigrations);

        await dbContext.Database.MigrateAsync(cancellationToken);

        var designTimeModel = dbContext.GetService<IDesignTimeModel>().Model;
        var statements = DirectoryDatabaseGrants.CreateStatements(designTimeModel, runtimeRole);

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(
            async token =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(token);
                foreach (var statement in statements)
                    await dbContext.Database.ExecuteSqlRawAsync(statement, token);
                await transaction.CommitAsync(token);
            },
            cancellationToken
        );
    }

    extension(DbContextOptionsBuilder builder)
    {
        /// <summary>
        /// Configures the options to use Npgsql against <paramref name="connectionString"/>, pointing migrations at
        /// this (the directory host) assembly.
        /// </summary>
        /// <param name="connectionString">The Postgres connection string.</param>
        /// <returns>The same <see cref="DbContextOptionsBuilder"/> instance for method chaining.</returns>
        public DbContextOptionsBuilder UseDirectoryNpgsql(string connectionString)
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder
                .UseNpgsql(
                    connectionString,
                    npgsql =>
                    {
                        npgsql.MigrationsAssembly(MigrationsAssembly);
                        npgsql.EnableRetryOnFailure();
                    }
                )
                .ReplaceService<IModelCustomizer, DirectoryNpgsqlModelCustomizer>();
        }
    }
}
