// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Xunit;

namespace NCode.Identity.OpenId.Host.Database.Tests;

public sealed class HostDatabaseRuntimeRoleTests
{
    private const string Key = "HostDatabase:RuntimeRole";

    private static IConfiguration CreateConfiguration(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))
            )
            .Build();

    #region Resolve Tests

    [Fact]
    public void Resolve_ValidRole_ReturnsIt()
    {
        var configuration = CreateConfiguration((Key, "ncode_identity_app"));

        var result = HostDatabaseRuntimeRole.Resolve(configuration);

        Assert.Equal("ncode_identity_app", result);
    }

    [Fact]
    public void Resolve_SurroundingWhitespace_ReturnsTrimmedRole()
    {
        var configuration = CreateConfiguration((Key, "  ncode_identity_app\t"));

        var result = HostDatabaseRuntimeRole.Resolve(configuration);

        Assert.Equal("ncode_identity_app", result);
    }

    [Fact]
    public void Resolve_Missing_ThrowsNamingTheKey()
    {
        var configuration = CreateConfiguration();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HostDatabaseRuntimeRole.Resolve(configuration)
        );

        Assert.Contains(Key, exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_Empty_ThrowsNamingTheKey(string value)
    {
        var configuration = CreateConfiguration((Key, value));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HostDatabaseRuntimeRole.Resolve(configuration)
        );

        Assert.Contains(Key, exception.Message);
    }

    [Theory]
    [InlineData("NCode_App")]
    [InlineData("\"ncode_app\"")]
    [InlineData("app\"; drop")]
    public void Resolve_InvalidRole_ThrowsNamingTheKeyButNotTheValue(string value)
    {
        var configuration = CreateConfiguration((Key, value));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HostDatabaseRuntimeRole.Resolve(configuration)
        );

        Assert.Contains(Key, exception.Message);
        Assert.DoesNotContain(value, exception.Message);
    }

    #endregion

    #region IsValid Tests

    [Theory]
    [InlineData("ncode_identity_app")]
    [InlineData("_app")]
    [InlineData("app1")]
    public void IsValid_ValidRole_ReturnsTrue(string role)
    {
        var result = HostDatabaseRuntimeRole.IsValid(role);

        Assert.True(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("app\"; drop")]
    [InlineData("App")]
    [InlineData("1app")]
    [InlineData("app\n")]
    public void IsValid_InvalidRole_ReturnsFalse(string role)
    {
        var result = HostDatabaseRuntimeRole.IsValid(role);

        Assert.False(result);
    }

    #endregion
}
