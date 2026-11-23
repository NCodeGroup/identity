// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class LocalAccountCodeMailTests : BaseTests
{
    #region ToString Tests

    [Fact]
    public void ToString_Always_LeavesOutTheCodeAndTheAddress()
    {
        var mail = new LocalAccountCodeMail(
            "user@example.com",
            null,
            "Subject",
            "{0} {1}",
            "en",
            123456,
            10
        );

        Assert.Equal(
            "LocalAccountCodeMail { LanguageTag = en, LifetimeMinutes = 10 }",
            mail.ToString()
        );
    }

    #endregion
}
