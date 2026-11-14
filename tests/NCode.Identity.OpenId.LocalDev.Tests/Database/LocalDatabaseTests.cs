// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.LocalDev.Database;

namespace NCode.Identity.OpenId.LocalDev.Tests.Database;

public class LocalDatabaseTests
{
    private const string Password = "Abc123-_xyzABC123-_xyzABC123-_xyzABC12345";

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
