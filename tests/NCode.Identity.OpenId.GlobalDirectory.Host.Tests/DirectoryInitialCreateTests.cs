// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using NCode.Identity.OpenId.Host.Database;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Tests;

public sealed class DirectoryInitialCreateTests
{
    private const string InitialCreate = "InitialCreate";
    private const string MigrationsNamespace =
        "NCode.Identity.OpenId.GlobalDirectory.Host.Migrations.Directory";

    private static IEnumerable<Type> GetMigrationTypes() =>
        typeof(DirectoryDatabase)
            .Assembly.GetTypes()
            .Where(type =>
                typeof(Migration).IsAssignableFrom(type)
                && type is { IsAbstract: false }
                && type.GetCustomAttribute<MigrationAttribute>() is not null
            )
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

    private static Migration CreateInitialCreate()
    {
        var type = typeof(DirectoryDatabase).Assembly.GetType(
            $"{MigrationsNamespace}.{InitialCreate}",
            throwOnError: true
        )!;
        var migration = (Migration)Activator.CreateInstance(type)!;
        migration.ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
        return migration;
    }

    #region DirectoryMigrations Tests

    [Fact]
    public void DirectoryMigrations_WhenEnumerated_HoldOnlyInitialCreate()
    {
        var migrations = GetMigrationTypes().ToList();

        var migration = Assert.Single(migrations);
        Assert.Equal(MigrationsNamespace, migration.Namespace);
        Assert.Equal(InitialCreate, migration.Name);
        var id = migration.GetCustomAttribute<MigrationAttribute>()!.Id;
        Assert.EndsWith($"_{InitialCreate}", id, StringComparison.Ordinal);
    }

    #endregion

    #region InitialCreate Tests

    [Fact]
    public void InitialCreate_WhenRead_ChangesNoData()
    {
        var migration = CreateInitialCreate();

        var dataChanges = ExpandOnlyRule.FindDataChanges([
            .. migration.UpOperations,
            .. migration.DownOperations,
        ]);

        Assert.True(dataChanges.Count == 0, string.Join(" ", dataChanges));
    }

    [Fact]
    public void InitialCreate_WhenRead_OnlyExpands()
    {
        var migration = CreateInitialCreate();

        var contracting = ExpandOnlyRule.FindContractingOperations(migration.UpOperations);

        Assert.True(contracting.Count == 0, string.Join(" ", contracting));
    }

    #endregion
}
