// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NCode.Identity.OpenId.Persistence.EntityFramework.Entities;
using Xunit;

namespace NCode.Identity.OpenId.Host.Database.Tests;

public sealed class RowLevelSecurityTests
{
    private const string Role = "identity_runtime";

    private const string Expression =
        "current_setting('app.unscoped', true) = 'on' "
        + "OR \"NormalizedTenantId\" = current_setting('app.tenant_id', true)";

    private static IModel CreateHostModel()
    {
        using var context = new OpenIdDbContextDesignTimeFactory().CreateDbContext([]);
        return context.Model;
    }

    private static string QualifiedName(IEntityType entityType)
    {
        var table = Quote(entityType.GetTableName()!);
        return entityType.GetSchema() is { } schema ? $"{Quote(schema)}.{table}" : table;
    }

    private static string Quote(string identifier) => $"\"{identifier}\"";

    #region CreateStatements Tests

    [Fact]
    public void CreateStatements_WhenModelHasTenantTables_CoversEveryTenantTable()
    {
        var model = CreateHostModel();
        var tenantTables = model
            .GetEntityTypes()
            .Where(entityType => typeof(ISupportTenantEntity).IsAssignableFrom(entityType.ClrType))
            .Select(QualifiedName)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(tenantTables.Count > 1);

        var statements = RowLevelSecurity.CreateStatements(model, Role);

        foreach (var table in tenantTables)
        {
            Assert.Contains($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY", statements);
            Assert.Contains($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY", statements);
            Assert.Contains($"DROP POLICY IF EXISTS tenant_isolation ON {table}", statements);
            Assert.Contains(
                $"CREATE POLICY tenant_isolation ON {table} USING ({Expression}) WITH CHECK ({Expression})",
                statements
            );
            Assert.Contains(
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON {table} TO \"{Role}\"",
                statements
            );
        }

        Assert.Contains("ALTER TABLE \"Clients\" ENABLE ROW LEVEL SECURITY", statements);
        Assert.Contains("ALTER TABLE \"LocalAccounts\" ENABLE ROW LEVEL SECURITY", statements);
        Assert.Equal(
            tenantTables.Count,
            statements.Count(statement =>
                statement.EndsWith(" ENABLE ROW LEVEL SECURITY", StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    public void CreateStatements_WhenTableHasNoTenant_EmitsOnlyTheGrant()
    {
        var model = CreateHostModel();
        var tenantEntity = Assert.Single(
            model.GetEntityTypes(),
            entityType => entityType.GetTableName() == "Tenants"
        );
        Assert.False(typeof(ISupportTenantEntity).IsAssignableFrom(tenantEntity.ClrType));

        var statements = RowLevelSecurity.CreateStatements(model, Role);

        var forTable = statements.Where(statement => statement.Contains("\"Tenants\"")).ToList();
        var grant = Assert.Single(forTable);
        Assert.Equal($"GRANT SELECT, INSERT, UPDATE, DELETE ON \"Tenants\" TO \"{Role}\"", grant);
    }

    [Fact]
    public void CreateStatements_WhenCalledTwice_ReturnsTheSameStatementsOrderedByTable()
    {
        var model = CreateHostModel();

        var first = RowLevelSecurity.CreateStatements(model, Role);
        var second = RowLevelSecurity.CreateStatements(model, Role);

        Assert.Equal(first, second);
        var grantedTables = first
            .Where(statement => statement.StartsWith("GRANT SELECT", StringComparison.Ordinal))
            .Select(statement => statement.Split(" ON ")[1].Split(" TO ")[0])
            .ToList();
        Assert.Equal(grantedTables.Order(StringComparer.Ordinal), grantedTables);
    }

    [Fact]
    public void CreateStatements_WhenTableHasASchemaAndColumnName_ReadsThemFromTheModel()
    {
        var builder = new ModelBuilder();
        builder.Entity<TenantRow>(entity =>
        {
            entity.ToTable("Rows", "app");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.NormalizedTenantId).HasColumnName("tenant\"column");
        });
        builder.HasSequence("RowSequence", "app");
        builder.HasSequence("Another");
        var model = builder.FinalizeModel();

        var statements = RowLevelSecurity.CreateStatements(model, Role);

        const string expression =
            "current_setting('app.unscoped', true) = 'on' "
            + "OR \"tenant\"\"column\" = current_setting('app.tenant_id', true)";
        Assert.Equal(
            [
                "ALTER TABLE \"app\".\"Rows\" ENABLE ROW LEVEL SECURITY",
                "ALTER TABLE \"app\".\"Rows\" FORCE ROW LEVEL SECURITY",
                "DROP POLICY IF EXISTS tenant_isolation ON \"app\".\"Rows\"",
                $"CREATE POLICY tenant_isolation ON \"app\".\"Rows\" USING ({expression}) WITH CHECK ({expression})",
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON \"app\".\"Rows\" TO \"{Role}\"",
                $"GRANT USAGE ON SEQUENCE \"Another\" TO \"{Role}\"",
                $"GRANT USAGE ON SEQUENCE \"app\".\"RowSequence\" TO \"{Role}\"",
            ],
            statements
        );
    }

    [Fact]
    public void CreateStatements_WhenModelHasNoSequence_EmitsNoSequenceGrant()
    {
        var model = CreateHostModel();
        Assert.Empty(model.GetSequences());

        var statements = RowLevelSecurity.CreateStatements(model, Role);

        Assert.DoesNotContain(statements, statement => statement.Contains("SEQUENCE"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Runtime")]
    [InlineData("1runtime")]
    [InlineData("run-time")]
    [InlineData("run time")]
    [InlineData("runtime\"; DROP TABLE x; --")]
    [InlineData("runtime\n")]
    public void CreateStatements_WhenRoleIsInvalid_Throws(string role)
    {
        var model = CreateHostModel();

        var exception = Assert.Throws<ArgumentException>(() =>
            RowLevelSecurity.CreateStatements(model, role)
        );

        Assert.Equal("runtimeRole", exception.ParamName);
    }

    [Theory]
    [InlineData("_")]
    [InlineData("runtime")]
    [InlineData("_run_time_2")]
    public void CreateStatements_WhenRoleIsValid_QuotesTheRole(string role)
    {
        var model = CreateHostModel();

        var statements = RowLevelSecurity.CreateStatements(model, role);

        Assert.All(
            statements.Where(statement => statement.StartsWith("GRANT", StringComparison.Ordinal)),
            statement => Assert.EndsWith($" TO \"{role}\"", statement)
        );
    }

    [Fact]
    public void CreateStatements_WhenModelIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RowLevelSecurity.CreateStatements(null!, Role));
    }

    [Fact]
    public void CreateStatements_WhenRoleIsNull_Throws()
    {
        var model = CreateHostModel();

        Assert.Throws<ArgumentNullException>(() => RowLevelSecurity.CreateStatements(model, null!));
    }

    #endregion

    #region CreateGrantStatements Tests

    [Fact]
    public void CreateGrantStatements_WhenModelHasTenantTables_EmitsNoRowLevelSecurityStatement()
    {
        var model = CreateHostModel();

        var statements = RowLevelSecurity.CreateGrantStatements(model, Role);

        Assert.NotEmpty(statements);
        Assert.All(
            statements,
            statement =>
            {
                Assert.StartsWith("GRANT ", statement);
                Assert.DoesNotContain("ROW LEVEL SECURITY", statement);
            }
        );
        Assert.Contains(
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON \"Clients\" TO \"{Role}\"",
            statements
        );
    }

    [Fact]
    public void CreateGrantStatements_WhenModelHasSequences_GrantsUsageOnEverySequence()
    {
        var builder = new ModelBuilder();
        builder.Entity<TenantRow>(entity =>
        {
            entity.ToTable("Rows", "app");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.NormalizedTenantId);
        });
        builder.HasSequence("RowSequence", "app");
        var model = builder.FinalizeModel();

        var statements = RowLevelSecurity.CreateGrantStatements(model, Role);

        Assert.Equal(
            [
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON \"app\".\"Rows\" TO \"{Role}\"",
                $"GRANT USAGE ON SEQUENCE \"app\".\"RowSequence\" TO \"{Role}\"",
            ],
            statements
        );
    }

    [Fact]
    public void CreateStatements_WhenModelIsValid_EndsWithTheGrantStatements()
    {
        var model = CreateHostModel();

        var statements = RowLevelSecurity.CreateStatements(model, Role);
        var grants = RowLevelSecurity.CreateGrantStatements(model, Role);

        Assert.Equal(grants, statements.Skip(statements.Count - grants.Count));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Runtime")]
    [InlineData("runtime\"; DROP TABLE x; --")]
    public void CreateGrantStatements_WhenRoleIsInvalid_Throws(string role)
    {
        var model = CreateHostModel();

        var exception = Assert.Throws<ArgumentException>(() =>
            RowLevelSecurity.CreateGrantStatements(model, role)
        );

        Assert.Equal("runtimeRole", exception.ParamName);
    }

    [Fact]
    public void CreateGrantStatements_WhenModelIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RowLevelSecurity.CreateGrantStatements(null!, Role)
        );
    }

    [Fact]
    public void CreateGrantStatements_WhenRoleIsNull_Throws()
    {
        var model = CreateHostModel();

        Assert.Throws<ArgumentNullException>(() =>
            RowLevelSecurity.CreateGrantStatements(model, null!)
        );
    }

    #endregion

    private sealed class TenantRow : ISupportTenantEntity
    {
        public long Id { get; set; }

        public string NormalizedTenantId { get; set; } = string.Empty;
    }
}
