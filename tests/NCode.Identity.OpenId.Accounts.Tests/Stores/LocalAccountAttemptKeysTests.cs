// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Accounts.Stores;

public sealed class LocalAccountAttemptKeysTests
{
    [Fact]
    public void ForAccount_PrefixesTheAccountId()
    {
        Assert.Equal("account:abc", LocalAccountAttemptKeys.ForAccount("abc"));
    }

    [Fact]
    public void ForIdentifier_PrefixesTheSubmittedText()
    {
        Assert.Equal("identifier:abc", LocalAccountAttemptKeys.ForIdentifier("abc"));
    }

    [Fact]
    public void ForAccountAndForIdentifier_WithOneText_YieldDistinctKeys()
    {
        Assert.NotEqual(
            LocalAccountAttemptKeys.ForAccount("abc"),
            LocalAccountAttemptKeys.ForIdentifier("abc")
        );
    }
}
