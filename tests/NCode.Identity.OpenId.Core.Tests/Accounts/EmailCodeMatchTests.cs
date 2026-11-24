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
            ConnectionId = LocalAccountStoreSetup.ConnectionId,
            LocalAccountId = localAccountId,
            UserName = "user",
            DisplayName = "Display Name",
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
        var match = new EmailCodeMatch(
            LocalAccountStoreSetup.ConnectionId,
            LocalAccountId,
            "USER@EXAMPLE.COM"
        );

        var result = match.IsFor(Account());

        Assert.True(result);
    }

    [Fact]
    public void IsFor_ForAnotherAccount_ReturnsFalse()
    {
        var match = new EmailCodeMatch(LocalAccountStoreSetup.ConnectionId, LocalAccountId, Email);

        var result = match.IsFor(Account(localAccountId: "account-2"));

        Assert.False(result);
    }

    [Fact]
    public void IsFor_ForAnotherConnection_ReturnsFalse()
    {
        var match = new EmailCodeMatch("connection-2", LocalAccountId, Email);

        var result = match.IsFor(Account());

        Assert.False(result);
    }

    [Fact]
    public void IsFor_WhenTheAddressDiffers_ReturnsFalse()
    {
        var match = new EmailCodeMatch(
            LocalAccountStoreSetup.ConnectionId,
            LocalAccountId,
            "old@example.com"
        );

        var result = match.IsFor(Account());

        Assert.False(result);
    }

    #endregion
}
