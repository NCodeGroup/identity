// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryDatabaseGrantsTests
{
    private const string RuntimeRole = "ncode_directory_app";

    private static IModel CreateModel()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseSqlite(connection)
            .Options;
        using var dbContext = new DirectoryDbContext(options);
        return dbContext.GetService<IDesignTimeModel>().Model;
    }

    #region CreateStatements Tests

    [Fact]
    public void CreateStatements_DirectoryModel_GrantsDataAccessOnEveryTable()
    {
        var model = CreateModel();

        var statements = DirectoryDatabaseGrants.CreateStatements(model, RuntimeRole);

        string[] expected =
        [
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Cells\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Changes\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Platforms\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"ReplicatedDocuments\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"RetiredIssuers\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Sequences\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Teams\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"TenantMoveOffers\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Tenants\" TO \"ncode_directory_app\"",
        ];
        Assert.Equal(expected, statements);
    }

    [Fact]
    public void CreateStatements_CalledTwice_ReturnsSameOrder()
    {
        var model = CreateModel();

        var first = DirectoryDatabaseGrants.CreateStatements(model, RuntimeRole);
        var second = DirectoryDatabaseGrants.CreateStatements(CreateModel(), RuntimeRole);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    [InlineData("a\"b")]
    public void CreateStatements_InvalidRole_Throws(string role)
    {
        var model = CreateModel();

        var exception = Assert.Throws<ArgumentException>(() =>
            DirectoryDatabaseGrants.CreateStatements(model, role)
        );

        Assert.Equal("runtimeRole", exception.ParamName);
    }

    [Fact]
    public void CreateStatements_ModelWithSchemaAndSequence_GrantsTheSchemaTheTableAndTheSequence()
    {
        var builder = new ModelBuilder();
        builder.Entity<SchemaRow>(entity =>
        {
            entity.ToTable("Rows", "dir");
            entity.HasKey(x => x.Id);
        });
        builder.HasSequence("Ids", "dir");
        var model = builder.FinalizeModel();

        var statements = DirectoryDatabaseGrants.CreateStatements(model, RuntimeRole);

        string[] expected =
        [
            "GRANT USAGE ON SCHEMA \"dir\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"dir\".\"Rows\" TO \"ncode_directory_app\"",
            "GRANT USAGE ON SEQUENCE \"dir\".\"Ids\" TO \"ncode_directory_app\"",
        ];
        Assert.Equal(expected, statements);
    }

    #endregion

    private sealed class SchemaRow
    {
        public Guid Id { get; set; }
    }
}
