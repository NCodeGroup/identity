// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Connections;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Connections;

public class ConnectionNameRulesTests
{
    [Theory]
    [InlineData("local")]
    [InlineData("Corp-AD")]
    [InlineData("a")]
    [InlineData("9_x-y")]
    public void IsValid_WhenNameIsWellFormed_ReturnsTrue(string name)
    {
        Assert.True(ConnectionNameRules.IsValid(name));
    }

    [Fact]
    public void IsValid_WhenNameHasMaxLength_ReturnsTrue()
    {
        Assert.True(ConnectionNameRules.IsValid(new string('a', ConnectionNameRules.MaxLength)));
    }

    [Fact]
    public void IsValid_WhenNameIsTooLong_ReturnsFalse()
    {
        Assert.False(
            ConnectionNameRules.IsValid(new string('a', ConnectionNameRules.MaxLength + 1))
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-local")]
    [InlineData("_local")]
    [InlineData("a b")]
    [InlineData("café")]
    [InlineData("local\n")]
    public void IsValid_WhenNameIsMalformed_ReturnsFalse(string? name)
    {
        Assert.False(ConnectionNameRules.IsValid(name));
    }
}
