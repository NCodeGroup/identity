// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.LocalDev.Database;
using Npgsql;

namespace NCode.Identity.OpenId.LocalDev.Tests.Database;

public class LocalDatabaseTests
{
    private const string Password = "Abc123-_xyzABC123-_xyzABC123-_xyzABC12345";

    private const string ServerConnectionString =
        $"Host=localhost;Port=15432;Username=postgres;Password={Password}";

    #region RuntimeRole Tests

    [Fact]
    public void RuntimeRole_Always_IsNcodeIdentityApp()
    {
        Assert.Equal("ncode_identity_app", LocalDatabase.RuntimeRole);
    }

    #endregion

    #region RoleScript Tests

    [Fact]
    public void RoleScript_WhenValid_IsOneDoStatement()
    {
        var script = LocalDatabase.RoleScript(LocalDatabase.RuntimeRole, Password);

        Assert.StartsWith("DO $$", script);
        Assert.EndsWith("$$", script);
        Assert.Equal(2, CountOccurrences(script, "$$"));
        Assert.Equal(1, CountOccurrences(script, "DO $$"));
    }

    [Fact]
    public void RoleScript_WhenValid_CreatesTheRoleWhenMissing()
    {
        var script = LocalDatabase.RoleScript(LocalDatabase.RuntimeRole, Password);

        Assert.Contains("WHERE rolname = 'ncode_identity_app'", script);
        Assert.Contains(
            $"CREATE ROLE \"ncode_identity_app\" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{Password}'",
            script
        );
    }

    [Fact]
    public void RoleScript_WhenValid_AltersTheRoleWhenPresent()
    {
        var script = LocalDatabase.RoleScript(LocalDatabase.RuntimeRole, Password);

        Assert.Contains(
            $"ALTER ROLE \"ncode_identity_app\" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{Password}'",
            script
        );
    }

    [Theory]
    [InlineData("pass'word")]
    [InlineData("pass\"word")]
    [InlineData("pass word")]
    [InlineData("pass+word")]
    [InlineData("pass/word")]
    [InlineData("pass=")]
    [InlineData("pass$$word")]
    [InlineData("")]
    public void RoleScript_WhenPasswordIsNotUrlSafeBase64_Throws(string password)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            LocalDatabase.RoleScript(LocalDatabase.RuntimeRole, password)
        );

        Assert.Equal("password", exception.ParamName);
    }

    [Theory]
    [InlineData("app\"; drop")]
    [InlineData("App")]
    [InlineData("1app")]
    [InlineData("")]
    public void RoleScript_WhenRoleIsInvalid_Throws(string role)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            LocalDatabase.RoleScript(role, Password)
        );

        Assert.Equal("runtimeRole", exception.ParamName);
    }

    [Fact]
    public void RoleScript_WhenPasswordIsInvalid_DoesNotRevealIt()
    {
        const string password = "secret'value";

        var exception = Assert.Throws<ArgumentException>(() =>
            LocalDatabase.RoleScript(LocalDatabase.RuntimeRole, password)
        );

        Assert.DoesNotContain(password, exception.Message);
    }

    #endregion

    #region RuntimeDatabaseParts Tests

    [Fact]
    public void RuntimeDatabaseParts_WhenValid_NamesTheDatabaseRoleAndPassword()
    {
        var parts = LocalDatabase.RuntimeDatabaseParts(Password);

        Assert.Equal(3, parts.Count);
        Assert.Equal("openiddb", parts["OpenIdDatabase__Name"]);
        Assert.Equal("ncode_identity_app", parts["OpenIdDatabase__Username"]);
        Assert.Equal(Password, parts["OpenIdDatabase__Password"]);
    }

    [Theory]
    [InlineData("pass'word")]
    [InlineData("pass\"word")]
    [InlineData("pass word")]
    [InlineData("pass+word")]
    [InlineData("pass/word")]
    [InlineData("pass=")]
    [InlineData("pass$$word")]
    [InlineData("")]
    public void RuntimeDatabaseParts_WhenPasswordIsInvalid_Throws(string password)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            LocalDatabase.RuntimeDatabaseParts(password)
        );

        Assert.Equal("password", exception.ParamName);
        if (password.Length > 0)
        {
            Assert.DoesNotContain(password, exception.Message);
        }
    }

    #endregion

    #region MaintenanceConnectionString Tests

    [Fact]
    public void MaintenanceConnectionString_WhenServerNamesNoDatabase_OpensThePostgresDatabase()
    {
        var connectionString = LocalDatabase.MaintenanceConnectionString(ServerConnectionString);

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("postgres", builder.Database);
    }

    [Fact]
    public void MaintenanceConnectionString_WhenServerNamesAnotherDatabase_OpensThePostgresDatabase()
    {
        var connectionString = LocalDatabase.MaintenanceConnectionString(
            $"{ServerConnectionString};Database=openiddb"
        );

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("postgres", builder.Database);
    }

    [Fact]
    public void MaintenanceConnectionString_WhenValid_KeepsTheServersHostPortAndCredentials()
    {
        var connectionString = LocalDatabase.MaintenanceConnectionString(ServerConnectionString);

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("localhost", builder.Host);
        Assert.Equal(15432, builder.Port);
        Assert.Equal("postgres", builder.Username);
        Assert.Equal(Password, builder.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void MaintenanceConnectionString_WhenEmptyOrWhiteSpace_Throws(
        string serverConnectionString
    )
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            LocalDatabase.MaintenanceConnectionString(serverConnectionString)
        );

        Assert.Equal("serverConnectionString", exception.ParamName);
    }

    #endregion

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
