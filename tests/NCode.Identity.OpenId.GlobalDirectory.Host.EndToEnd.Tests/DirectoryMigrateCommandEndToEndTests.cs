// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.GlobalDirectory.Host.EndToEnd.Tests.Infrastructure;
using NCode.Identity.OpenId.GlobalDirectory.Server;
using Npgsql;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.EndToEnd.Tests;

/// <summary>
/// Proves on a real Postgres database that the directory's <c>migrate</c> command creates the schema through the schema
/// owner's connection, grants the runtime role the data access it needs, can run again, and refuses a database whose
/// migrations history records a migration the host does not hold.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DirectoryMigrateCommandEndToEndTests(PostgresFixture fixture)
{
    private const string UnknownMigrationId = "20261001000000_InitialCreate";
    private const string UnknownProductVersion = "10.0.0";

    private static IReadOnlyList<string> DataPrivileges { get; } =
    ["SELECT", "INSERT", "UPDATE", "DELETE"];

    #region MigrateAsync Tests

    [Fact]
    public async Task MigrateAsync_OnAnEmptyDatabase_CreatesTheSchemaAndGrantsTheRuntimeRole()
    {
        var connectionString = await fixture.CreateDatabaseAsync(CancellationToken.None);
        await using var serviceProvider = CreateServiceProvider(connectionString);

        await DirectoryDatabase.MigrateAsync(
            serviceProvider,
            PostgresFixture.RuntimeRole,
            CancellationToken.None
        );

        var tables = await GetModelTablesAsync(serviceProvider, CancellationToken.None);
        Assert.NotEmpty(tables);
        foreach (var table in tables)
        {
            foreach (var privilege in DataPrivileges)
            {
                var hasPrivilege = await HasTablePrivilegeAsync(
                    connectionString,
                    table,
                    privilege,
                    CancellationToken.None
                );

                Assert.NotNull(hasPrivilege);
                Assert.True(
                    hasPrivilege,
                    $"{PostgresFixture.RuntimeRole} lacks {privilege} on {table}."
                );
            }
        }
    }

    [Fact]
    public async Task MigrateAsync_WhenRunTwice_Succeeds()
    {
        var connectionString = await fixture.CreateDatabaseAsync(CancellationToken.None);
        await using var serviceProvider = CreateServiceProvider(connectionString);
        await DirectoryDatabase.MigrateAsync(
            serviceProvider,
            PostgresFixture.RuntimeRole,
            CancellationToken.None
        );

        await DirectoryDatabase.MigrateAsync(
            serviceProvider,
            PostgresFixture.RuntimeRole,
            CancellationToken.None
        );

        var factory = serviceProvider.GetRequiredService<IDbContextFactory<DirectoryDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        var migrations = dbContext.Database.GetMigrations();
        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync(
            CancellationToken.None
        );
        Assert.Equal(migrations, appliedMigrations);
    }

    [Fact]
    public async Task MigrateAsync_WhenTheHistoryHoldsAnUnknownMigration_Throws()
    {
        var connectionString = await fixture.CreateDatabaseAsync(CancellationToken.None);
        await using var serviceProvider = CreateServiceProvider(connectionString);
        await RecordUnknownMigrationAsync(serviceProvider, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DirectoryDatabase
                .MigrateAsync(serviceProvider, PostgresFixture.RuntimeRole, CancellationToken.None)
                .AsTask()
        );

        Assert.Contains(UnknownMigrationId, exception.Message);
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<DirectoryDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync(
            CancellationToken.None
        );
        Assert.Equal([UnknownMigrationId], appliedMigrations);
    }

    #endregion

    private static ServiceProvider CreateServiceProvider(string connectionString)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDirectoryDatabase(connectionString);
        return serviceCollection.BuildServiceProvider();
    }

    private static async ValueTask<IReadOnlyList<string>> GetModelTablesAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken
    )
    {
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<DirectoryDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);

        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var entityTypes = model.GetEntityTypes();
        return
        [
            .. entityTypes
                .Select(entityType => entityType.GetTableName())
                .OfType<string>()
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Records a migration that the host does not hold in the database's migrations history, as an <c>InitialCreate</c>
    /// of another model would have left it, creating the history table the way the migrations do.
    /// </summary>
    private static async ValueTask RecordUnknownMigrationAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken
    )
    {
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<DirectoryDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);

        var historyRepository = dbContext.GetService<IHistoryRepository>();
        var createScript = historyRepository.GetCreateIfNotExistsScript();
        var row = new HistoryRow(UnknownMigrationId, UnknownProductVersion);
        var insertScript = historyRepository.GetInsertScript(row);

        await dbContext.Database.ExecuteSqlRawAsync(createScript, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(insertScript, cancellationToken);
    }

    /// <summary>
    /// Asks the database whether the runtime role holds <paramref name="privilege"/> on <paramref name="table"/>, which
    /// the table's owner granted through the migrate command.
    /// </summary>
    /// <returns>The answer, or <see langword="null"/> when the database has no such table.</returns>
    private static async ValueTask<bool?> HasTablePrivilegeAsync(
        string connectionString,
        string table,
        string privilege,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT has_table_privilege(@role, c.oid, @privilege)
            FROM pg_class AS c
            JOIN pg_namespace AS n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = @table AND c.relkind = 'r'
            """,
            connection
        );
        command.Parameters.AddWithValue("role", PostgresFixture.RuntimeRole);
        command.Parameters.AddWithValue("privilege", privilege);
        command.Parameters.AddWithValue("table", table);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as bool?;
    }
}
