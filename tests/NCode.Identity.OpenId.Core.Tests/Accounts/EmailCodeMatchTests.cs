// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Accounts.DataContracts;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class EmailCodeMatchTests : BaseTests
{
    private const string LocalAccountId = "account-1";
    private const string Email = "user@example.com";

    private static PersistedLocalAccount Account(
        string localAccountId = LocalAccountId,
        string? email = Email
    ) =>
        new()
        {
            TenantId = "tenant-1",
            LocalAccountId = localAccountId,
            UserName = "user",
            Email = email,
            EmailVerified = true,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    #region IsFor Tests

    [Fact]
    public void IsFor_WhenTheAccountAndAddressMatchIgnoringCase_ReturnsTrue()
    {
        var match = new EmailCodeMatch(LocalAccountId, "USER@EXAMPLE.COM");

        var result = match.IsFor(Account());

        Assert.True(result);
    }

    [Fact]
    public void IsFor_ForAnotherAccount_ReturnsFalse()
    {
        var match = new EmailCodeMatch(LocalAccountId, Email);

        var result = match.IsFor(Account(localAccountId: "account-2"));

        Assert.False(result);
    }

    [Fact]
    public void IsFor_WhenTheAddressDiffers_ReturnsFalse()
    {
        var match = new EmailCodeMatch(LocalAccountId, "old@example.com");

        var result = match.IsFor(Account());

        Assert.False(result);
    }

    #endregion
}
