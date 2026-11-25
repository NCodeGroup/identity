// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class CellKindRulesTests : BaseTests
{
    #region IsValidKind Tests

    [Theory]
    [InlineData("shared")]
    [InlineData("Dedicated_Eu")]
    [InlineData("k")]
    public void IsValidKind_WithANonBlankKind_ReturnsTrue(string kind)
    {
        Assert.True(CellKindRules.IsValidKind(kind));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidKind_WithANullOrBlankKind_ReturnsFalse(string? kind)
    {
        Assert.False(CellKindRules.IsValidKind(kind));
    }

    [Fact]
    public void IsValidKind_WithTheLongestKind_ReturnsTrue()
    {
        Assert.True(CellKindRules.IsValidKind(new string('k', CellKindRules.MaxKindLength)));
    }

    [Fact]
    public void IsValidKind_WithAKindOverTheLimit_ReturnsFalse()
    {
        Assert.False(CellKindRules.IsValidKind(new string('k', CellKindRules.MaxKindLength + 1)));
    }

    #endregion
}
