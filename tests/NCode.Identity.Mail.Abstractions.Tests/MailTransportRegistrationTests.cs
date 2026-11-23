// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.Mail.Abstractions.Tests;

public class MailTransportRegistrationTests : BaseTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithAName_KeepsIt()
    {
        var registration = new MailTransportRegistration("Mail:Smtp");

        Assert.Equal("Mail:Smtp", registration.Name);
    }

    [Fact]
    public void Constructor_WithAnEmptyName_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new MailTransportRegistration(string.Empty)
        );

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithANullName_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new MailTransportRegistration(null!)
        );

        Assert.Equal("name", exception.ParamName);
    }

    #endregion
}
