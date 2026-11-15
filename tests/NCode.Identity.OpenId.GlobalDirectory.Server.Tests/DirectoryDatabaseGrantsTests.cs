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

    #region IsValidRole Tests

    [Theory]
    [InlineData("ncode_directory_app")]
    [InlineData("_role")]
    [InlineData("role1")]
    public void IsValidRole_LowerCaseIdentifier_ReturnsTrue(string role)
    {
        Assert.True(DirectoryDatabaseGrants.IsValidRole(role));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("1role")]
    [InlineData("a b")]
    [InlineData("a\"b")]
    [InlineData("role\n")]
    public void IsValidRole_InvalidName_ReturnsFalse(string role)
    {
        Assert.False(DirectoryDatabaseGrants.IsValidRole(role));
    }

    #endregion

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
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"RetiredIssuers\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Sequences\" TO \"ncode_directory_app\"",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Teams\" TO \"ncode_directory_app\"",
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

    #endregion
}
