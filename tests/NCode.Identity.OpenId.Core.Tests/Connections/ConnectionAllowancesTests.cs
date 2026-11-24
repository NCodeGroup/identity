// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Connections;

public class ConnectionAllowancesTests
{
    private static IReadOnlySettingCollection CreateSettings(string[]? allowed)
    {
        var mock = new Mock<IReadOnlySettingCollection>(MockBehavior.Strict);
        IReadOnlyCollection<string>? allowedValue = allowed;
        mock.Setup(x =>
                x.TryGetValue(It.IsAny<SettingKey<IReadOnlyCollection<string>>>(), out allowedValue)
            )
            .Returns(allowed is not null);
        return mock.Object;
    }

    [Fact]
    public void IsAllowed_WhenAllowedConnectionsIsUnset_ReturnsTrue()
    {
        var settings = CreateSettings(null);

        Assert.True(ConnectionAllowances.IsAllowed(settings, "local"));
    }

    [Fact]
    public void IsAllowed_WhenAllowedConnectionsIsEmpty_ReturnsFalse()
    {
        var settings = CreateSettings([]);

        Assert.False(ConnectionAllowances.IsAllowed(settings, "local"));
    }

    [Fact]
    public void IsAllowed_WhenNameIsListedInAnotherCase_ReturnsTrue()
    {
        var settings = CreateSettings(["Local", "corp"]);

        Assert.True(ConnectionAllowances.IsAllowed(settings, "LOCAL"));
    }

    [Fact]
    public void IsAllowed_WhenNameIsNotListed_ReturnsFalse()
    {
        var settings = CreateSettings(["corp"]);

        Assert.False(ConnectionAllowances.IsAllowed(settings, "local"));
    }

    [Fact]
    public void IsAllowed_WhenNameIsNullAndAllowedConnectionsIsUnset_ReturnsTrue()
    {
        var settings = CreateSettings(null);

        Assert.True(ConnectionAllowances.IsAllowed(settings, null));
    }

    [Fact]
    public void IsAllowed_WhenNameIsNullAndAllowedConnectionsIsSet_ReturnsFalse()
    {
        var settings = CreateSettings(["local"]);

        Assert.False(ConnectionAllowances.IsAllowed(settings, null));
    }
}
