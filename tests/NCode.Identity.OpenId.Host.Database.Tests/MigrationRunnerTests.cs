// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Xunit;

namespace NCode.Identity.OpenId.Host.Database.Tests;

public sealed class MigrationRunnerTests
{
    private const string Role = "identity_runtime";
    private const string ContextName = "OpenIdDbContext";
    private const string InitialCreate = "20261124203828_InitialCreate";
    private const string UnknownInitialCreate = "20261001000000_InitialCreate";

    #region ThrowIfUnknownMigrationsApplied Tests

    [Fact]
    public void ThrowIfUnknownMigrationsApplied_WhenNoneApplied_DoesNotThrow()
    {
        MigrationRunner.ThrowIfUnknownMigrationsApplied(ContextName, [], [InitialCreate]);
    }

    [Fact]
    public void ThrowIfUnknownMigrationsApplied_WhenAppliedIdIsKnown_DoesNotThrow()
    {
        MigrationRunner.ThrowIfUnknownMigrationsApplied(
            ContextName,
            [InitialCreate],
            [InitialCreate]
        );
    }

    [Fact]
    public void ThrowIfUnknownMigrationsApplied_WhenAppliedIdIsUnknown_ThrowsWithTheReset()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MigrationRunner.ThrowIfUnknownMigrationsApplied(
                ContextName,
                [UnknownInitialCreate],
                [InitialCreate]
            )
        );

        Assert.Equal(
            $"The database's {ContextName} migrations history records {UnknownInitialCreate}, which this host does "
                + $"not hold, so its {ContextName} migrations cannot be applied to it. Each host database context has "
                + "one InitialCreate, which is re-scaffolded with a fresh identifier on each model change. Stop the "
                + "AppHost and reset the local database with build/dev-db-reset.ps1; the next AppHost run creates "
                + "the schema and seeds the development data.",
            exception.Message
        );
    }

    #endregion

    #region CreateAccessStatements Tests

    [Fact]
    public void CreateAccessStatements_WhenModelsAreValid_PutsRowLevelSecurityBeforeTheDataProtectionGrants()
    {
        using var openIdContext = new OpenIdDbContextDesignTimeFactory().CreateDbContext([]);
        using var dataProtectionContext =
            new DeveloperDataProtectionDbContextDesignTimeFactory().CreateDbContext([]);

        var statements = MigrationRunner.CreateAccessStatements(
            openIdContext.Model,
            dataProtectionContext.Model,
            Role
        );

        var openId = RowLevelSecurity.CreateStatements(openIdContext.Model, Role);
        var dataProtection = RowLevelSecurity.CreateGrantStatements(
            dataProtectionContext.Model,
            Role
        );
        Assert.NotEmpty(dataProtection);
        Assert.Equal([.. openId, .. dataProtection], statements);
        Assert.Contains(
            "ALTER TABLE \"Clients\" ENABLE ROW LEVEL SECURITY",
            statements.Take(openId.Count)
        );
        Assert.DoesNotContain(
            dataProtection,
            statement => statement.Contains("ROW LEVEL SECURITY", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void CreateAccessStatements_WhenModelsAreValid_GrantsEveryTableOfBothModels()
    {
        using var openIdContext = new OpenIdDbContextDesignTimeFactory().CreateDbContext([]);
        using var dataProtectionContext =
            new DeveloperDataProtectionDbContextDesignTimeFactory().CreateDbContext([]);

        var statements = MigrationRunner.CreateAccessStatements(
            openIdContext.Model,
            dataProtectionContext.Model,
            Role
        );

        foreach (var model in new[] { openIdContext.Model, dataProtectionContext.Model })
        {
            var tables = model
                .GetEntityTypes()
                .Where(entityType => entityType.GetTableName() is not null)
                .Select(entityType =>
                {
                    var table = $"\"{entityType.GetTableName()}\"";
                    return entityType.GetSchema() is { } schema ? $"\"{schema}\".{table}" : table;
                })
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert.NotEmpty(tables);

            foreach (var table in tables)
                Assert.Contains(
                    $"GRANT SELECT, INSERT, UPDATE, DELETE ON {table} TO \"{Role}\"",
                    statements
                );
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Runtime")]
    [InlineData("runtime\"; DROP TABLE x; --")]
    public void CreateAccessStatements_WhenRoleIsInvalid_Throws(string role)
    {
        using var openIdContext = new OpenIdDbContextDesignTimeFactory().CreateDbContext([]);
        using var dataProtectionContext =
            new DeveloperDataProtectionDbContextDesignTimeFactory().CreateDbContext([]);

        var exception = Assert.Throws<ArgumentException>(() =>
            MigrationRunner.CreateAccessStatements(
                openIdContext.Model,
                dataProtectionContext.Model,
                role
            )
        );

        Assert.Equal("runtimeRole", exception.ParamName);
    }

    #endregion
}
