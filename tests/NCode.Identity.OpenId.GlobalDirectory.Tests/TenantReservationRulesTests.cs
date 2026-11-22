// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class TenantReservationRulesTests : BaseTests
{
    #region GetTeamId Tests

    [Fact]
    public void GetTeamId_WhenTheTeamIsThePlatformsOwn_ReturnsNull()
    {
        var result = TenantReservationRules.GetTeamId("platform", "platform");

        Assert.Null(result);
    }

    [Fact]
    public void GetTeamId_WhenTheTeamIsAnother_ReturnsIt()
    {
        var result = TenantReservationRules.GetTeamId("acme", "platform");

        Assert.Equal("acme", result);
    }

    #endregion
}
