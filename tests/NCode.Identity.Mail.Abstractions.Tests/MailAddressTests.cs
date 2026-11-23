// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.Mail.Abstractions.Tests;

public class MailAddressTests : BaseTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithAnAddressAndADisplayName_SetsBoth()
    {
        var address = new MailAddress("user@example.com", "A User");

        Assert.Equal("user@example.com", address.Address);
        Assert.Equal("A User", address.DisplayName);
    }

    [Fact]
    public void Constructor_WithoutADisplayName_LeavesItNull()
    {
        var address = new MailAddress("user@example.com");

        Assert.Null(address.DisplayName);
    }

    [Fact]
    public void Constructor_WithAnEmptyAddress_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new MailAddress(string.Empty));

        Assert.Equal("address", exception.ParamName);
    }

    [Theory]
    [InlineData("user.example.com")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public void Constructor_WithoutAnInnerAtSign_Throws(string address)
    {
        var exception = Assert.Throws<ArgumentException>(() => new MailAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(
            "The address must hold an '@' between its first and last characters.",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("user\r\n@example.com")]
    [InlineData("user@exa\0mple.com")]
    [InlineData("us\u007Fer@example.com")]
    public void Constructor_WithAControlCharacterInTheAddress_Throws(string address)
    {
        var exception = Assert.Throws<ArgumentException>(() => new MailAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(
            "The address must not hold a control character.",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("A\r\nBcc: other@example.com")]
    [InlineData("A\tUser")]
    [InlineData("A\u007FUser")]
    public void Constructor_WithAControlCharacterInTheDisplayName_Throws(string displayName)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new MailAddress("user@example.com", displayName)
        );

        Assert.Equal("displayName", exception.ParamName);
        Assert.StartsWith(
            "The display name must not hold a control character.",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Constructor_WithANonAsciiDisplayName_Succeeds()
    {
        var address = new MailAddress("user@example.com", "Zoë Ångström");

        Assert.Equal("Zoë Ångström", address.DisplayName);
    }

    #endregion
}
