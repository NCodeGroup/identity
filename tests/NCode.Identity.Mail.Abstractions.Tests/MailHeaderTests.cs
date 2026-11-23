// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.Mail.Abstractions.Tests;

public class MailHeaderTests : BaseTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithANameAndAValue_SetsBoth()
    {
        var header = new MailHeader("X-Campaign", "spring sale");

        Assert.Equal("X-Campaign", header.Name);
        Assert.Equal("spring sale", header.Value);
    }

    [Fact]
    public void Constructor_WithAnEmptyValue_Succeeds()
    {
        var header = new MailHeader("X-Campaign", string.Empty);

        Assert.Equal(string.Empty, header.Value);
    }

    [Fact]
    public void Constructor_WithAnEmptyName_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new MailHeader(string.Empty, "value")
        );

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("X Campaign")]
    [InlineData("X-Campaign:")]
    [InlineData("X-Camp\naign")]
    [InlineData("X-Cämpaign")]
    [InlineData("X-Camp\u007Faign")]
    public void Constructor_WithANameOutsideAsciiPrintable_Throws(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new MailHeader(name, "value"));

        Assert.Equal("name", exception.ParamName);
        Assert.StartsWith(
            "The name must hold only ASCII characters from 33 to 126 and no colon.",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("a\rb")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    public void Constructor_WithACarriageReturnLineFeedOrNulInTheValue_Throws(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => new MailHeader("X-Campaign", value));

        Assert.Equal("value", exception.ParamName);
        Assert.StartsWith(
            "The value must not hold a carriage return, a line feed or a NUL.",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    #endregion
}
