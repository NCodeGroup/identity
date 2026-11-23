// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.Mail.Mime.Tests;

public class MimeMailAddressesTests : BaseTests
{
    #region IsMailAddress Tests

    [Fact]
    public void IsMailAddress_WithOneAddress_ReturnsTrue()
    {
        Assert.True(MimeMailAddresses.IsMailAddress("noreply@example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("noreply")]
    public void IsMailAddress_WithoutOneAddress_ReturnsFalse(string? value)
    {
        Assert.False(MimeMailAddresses.IsMailAddress(value));
    }

    #endregion
}
