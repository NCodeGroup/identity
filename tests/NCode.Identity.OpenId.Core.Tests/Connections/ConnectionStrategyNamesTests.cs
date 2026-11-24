// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Connections;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Connections;

public class ConnectionStrategyNamesTests
{
    [Fact]
    public void IsLocal_WhenStrategyIsLocal_ReturnsTrue()
    {
        Assert.True(ConnectionStrategyNames.IsLocal("local"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Local")]
    [InlineData("oidc")]
    public void IsLocal_WhenStrategyIsNotLocal_ReturnsFalse(string? strategy)
    {
        Assert.False(ConnectionStrategyNames.IsLocal(strategy));
    }
}
