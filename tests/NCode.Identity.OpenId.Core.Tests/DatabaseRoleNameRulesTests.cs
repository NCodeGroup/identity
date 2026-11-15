// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests;

public class DatabaseRoleNameRulesTests
{
    private const string Key = "HostDatabase:RuntimeRole";
    private const string ConnectsAs = "that the host connects as";

    #region IsValid Tests

    [Theory]
    [InlineData("ncode_identity_app")]
    [InlineData("ncode_directory_app")]
    [InlineData("_app")]
    [InlineData("_role")]
    [InlineData("app1")]
    [InlineData("role1")]
    public void IsValid_WhenLowerCaseIdentifier_ReturnsTrue(string role)
    {
        Assert.True(DatabaseRoleNameRules.IsValid(role));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("App")]
    [InlineData("1app")]
    [InlineData("1role")]
    [InlineData("a b")]
    [InlineData("a\"b")]
    [InlineData("app\"; drop")]
    [InlineData("app\n")]
    [InlineData("role\n")]
    public void IsValid_WhenNotLowerCaseIdentifier_ReturnsFalse(string role)
    {
        Assert.False(DatabaseRoleNameRules.IsValid(role));
    }

    [Fact]
    public void IsValid_WhenNull_ReturnsFalse()
    {
        Assert.False(DatabaseRoleNameRules.IsValid(null));
    }

    #endregion

    #region RequireRuntimeRole Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireRuntimeRole_WhenBlank_ThrowsNamingTheKey(string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseRoleNameRules.RequireRuntimeRole(value, Key, ConnectsAs)
        );

        Assert.Contains(Key, exception.Message, StringComparison.Ordinal);
        Assert.Contains(ConnectsAs, exception.Message, StringComparison.Ordinal);
        Assert.Contains("is not configured", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NCode_App")]
    [InlineData("\"ncode_app\"")]
    [InlineData("app\"; drop")]
    public void RequireRuntimeRole_WhenInvalid_ThrowsWithoutTheValue(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseRoleNameRules.RequireRuntimeRole(value, Key, ConnectsAs)
        );

        Assert.Contains(Key, exception.Message, StringComparison.Ordinal);
        Assert.Contains(DatabaseRoleNameRules.Pattern, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireRuntimeRole_WhenPadded_ReturnsTheTrimmedRole()
    {
        var result = DatabaseRoleNameRules.RequireRuntimeRole(
            "  ncode_identity_app\t",
            Key,
            ConnectsAs
        );

        Assert.Equal("ncode_identity_app", result);
    }

    #endregion
}
