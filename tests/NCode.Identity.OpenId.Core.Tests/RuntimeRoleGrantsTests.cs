// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests;

public class RuntimeRoleGrantsTests
{
    private const string Role = "identity_runtime";

    #region CreateStatements Tests

    [Fact]
    public void CreateStatements_WhenNamesAreUnordered_GrantsTheSchemasThenTheTablesThenTheSequencesInOrder()
    {
        DatabaseObjectName[] tables =
        [
            new("app", "B"),
            new(null, "Rows"),
            new("app", "A"),
            new(null, "Clients"),
        ];
        DatabaseObjectName[] sequences = [new("app", "RowSequence"), new(null, "Another")];

        var statements = RuntimeRoleGrants.CreateStatements(Role, tables, sequences);

        Assert.Equal(
            [
                "GRANT USAGE ON SCHEMA \"app\" TO \"identity_runtime\"",
                "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Clients\" TO \"identity_runtime\"",
                "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Rows\" TO \"identity_runtime\"",
                "GRANT SELECT, INSERT, UPDATE, DELETE ON \"app\".\"A\" TO \"identity_runtime\"",
                "GRANT SELECT, INSERT, UPDATE, DELETE ON \"app\".\"B\" TO \"identity_runtime\"",
                "GRANT USAGE ON SEQUENCE \"Another\" TO \"identity_runtime\"",
                "GRANT USAGE ON SEQUENCE \"app\".\"RowSequence\" TO \"identity_runtime\"",
            ],
            statements
        );
    }

    [Fact]
    public void CreateStatements_WhenOnlyASequenceIsInASchema_GrantsUsageOnThatSchema()
    {
        DatabaseObjectName[] tables = [new(null, "Rows")];
        DatabaseObjectName[] sequences = [new("ids", "RowSequence")];

        var statements = RuntimeRoleGrants.CreateStatements(Role, tables, sequences);

        Assert.Equal(
            [
                "GRANT USAGE ON SCHEMA \"ids\" TO \"identity_runtime\"",
                "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Rows\" TO \"identity_runtime\"",
                "GRANT USAGE ON SEQUENCE \"ids\".\"RowSequence\" TO \"identity_runtime\"",
            ],
            statements
        );
    }

    [Fact]
    public void CreateStatements_WhenANameIsGivenTwice_GrantsItOnce()
    {
        DatabaseObjectName[] tables = [new(null, "Rows"), new(null, "Rows")];
        DatabaseObjectName[] sequences = [new(null, "Ids"), new(null, "Ids")];

        var statements = RuntimeRoleGrants.CreateStatements(Role, tables, sequences);

        Assert.Equal(
            [
                "GRANT SELECT, INSERT, UPDATE, DELETE ON \"Rows\" TO \"identity_runtime\"",
                "GRANT USAGE ON SEQUENCE \"Ids\" TO \"identity_runtime\"",
            ],
            statements
        );
    }

    [Fact]
    public void CreateStatements_WhenANameHoldsADoubleQuote_QuotesIt()
    {
        DatabaseObjectName[] tables = [new("my\"schema", "my\"table")];

        var statements = RuntimeRoleGrants.CreateStatements(Role, tables, []);

        Assert.Equal(
            [
                "GRANT USAGE ON SCHEMA \"my\"\"schema\" TO \"identity_runtime\"",
                "GRANT SELECT, INSERT, UPDATE, DELETE ON \"my\"\"schema\".\"my\"\"table\" TO \"identity_runtime\"",
            ],
            statements
        );
    }

    [Fact]
    public void CreateStatements_WhenNothingIsNamed_ReturnsNoStatement()
    {
        var statements = RuntimeRoleGrants.CreateStatements(Role, [], []);

        Assert.Empty(statements);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Runtime")]
    [InlineData("1runtime")]
    [InlineData("run-time")]
    [InlineData("run time")]
    [InlineData("runtime\"; DROP TABLE x; --")]
    [InlineData("runtime\n")]
    [InlineData("Upper")]
    [InlineData("a b")]
    [InlineData("a\"b")]
    public void CreateStatements_WhenRoleIsInvalid_Throws(string role)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            RuntimeRoleGrants.CreateStatements(role, [], [])
        );

        Assert.Equal("runtimeRole", exception.ParamName);
    }

    [Fact]
    public void CreateStatements_WhenRoleIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            RuntimeRoleGrants.CreateStatements(null!, [], [])
        );

        Assert.Equal("runtimeRole", exception.ParamName);
    }

    [Fact]
    public void CreateStatements_WhenTablesIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            RuntimeRoleGrants.CreateStatements(Role, null!, [])
        );

        Assert.Equal("tables", exception.ParamName);
    }

    [Fact]
    public void CreateStatements_WhenSequencesIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            RuntimeRoleGrants.CreateStatements(Role, [], null!)
        );

        Assert.Equal("sequences", exception.ParamName);
    }

    #endregion
}
