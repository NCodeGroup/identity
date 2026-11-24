// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Connections;

namespace NCode.Identity.OpenId.Accounts.Stores;

public sealed class LocalAccountSignInIdentifierTests
{
    #region ForSubmitted Tests

    [Fact]
    public void ForSubmitted_WithEmailMode_SetsOnlyEmail()
    {
        var identifier = LocalAccountSignInIdentifier.ForSubmitted(
            "connection-1",
            LocalAccountIdentifiers.Email,
            "ada@example.com"
        );

        Assert.Equal("connection-1", identifier.ConnectionId);
        Assert.Null(identifier.UserName);
        Assert.Equal("ada@example.com", identifier.Email);
    }

    [Fact]
    public void ForSubmitted_WithUserNameMode_SetsOnlyUserName()
    {
        var identifier = LocalAccountSignInIdentifier.ForSubmitted(
            "connection-1",
            LocalAccountIdentifiers.UserName,
            "ada"
        );

        Assert.Equal("connection-1", identifier.ConnectionId);
        Assert.Equal("ada", identifier.UserName);
        Assert.Null(identifier.Email);
    }

    [Fact]
    public void ForSubmitted_WithEmailOrUserNameMode_SetsBoth()
    {
        var identifier = LocalAccountSignInIdentifier.ForSubmitted(
            "connection-1",
            LocalAccountIdentifiers.EmailOrUserName,
            "ada"
        );

        Assert.Equal("connection-1", identifier.ConnectionId);
        Assert.Equal("ada", identifier.UserName);
        Assert.Equal("ada", identifier.Email);
    }

    #endregion
}
