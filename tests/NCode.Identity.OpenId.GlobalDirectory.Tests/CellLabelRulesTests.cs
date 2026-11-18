// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class CellLabelRulesTests : BaseTests
{
    #region IsValidLabel Tests

    [Theory]
    [InlineData("a")]
    [InlineData("cell-a")]
    [InlineData("region-1")]
    [InlineData("eu-1")]
    public void IsValidLabel_WithALowercaseLabel_ReturnsTrue(string label)
    {
        Assert.True(CellLabelRules.IsValidLabel(label));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("West-Europe")]
    [InlineData("1cell")]
    [InlineData("cell-")]
    [InlineData("prod--eu")]
    [InlineData("prod-")]
    [InlineData("cell_a")]
    [InlineData("cell\n")]
    public void IsValidLabel_WithAnInvalidLabel_ReturnsFalse(string? label)
    {
        Assert.False(CellLabelRules.IsValidLabel(label));
    }

    [Fact]
    public void IsValidLabel_WithMaxLabelLengthCharacters_ReturnsTrue()
    {
        var label = "a" + new string('b', CellLabelRules.MaxLabelLength - 1);

        Assert.True(CellLabelRules.IsValidLabel(label));
    }

    [Fact]
    public void IsValidLabel_WithOneMoreThanMaxLabelLengthCharacters_ReturnsFalse()
    {
        var label = "a" + new string('b', CellLabelRules.MaxLabelLength);

        Assert.False(CellLabelRules.IsValidLabel(label));
    }

    #endregion
}
