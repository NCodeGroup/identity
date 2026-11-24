// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Connections;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Connections;

public class ConnectionRequestsTests
{
    [Fact]
    public void GetRequestedName_WhenParameterAndAcrValue_ReturnsParameter()
    {
        var name = ConnectionRequests.GetRequestedName("corp", ["idp:other"]);

        Assert.Equal("corp", name);
    }

    [Fact]
    public void GetRequestedName_WhenOnlyIdpAcrValue_ReturnsTextAfterPrefixOfFirst()
    {
        var name = ConnectionRequests.GetRequestedName(
            null,
            ["urn:other", "idp:corp", "idp:second"]
        );

        Assert.Equal("corp", name);
    }

    [Fact]
    public void GetRequestedName_WhenParameterIsEmpty_FallsBackToAcrValue()
    {
        var name = ConnectionRequests.GetRequestedName("", ["idp:corp"]);

        Assert.Equal("corp", name);
    }

    [Fact]
    public void GetRequestedName_WhenNothingNamesAConnection_ReturnsNull()
    {
        Assert.Null(ConnectionRequests.GetRequestedName(null, ["urn:other"]));
        Assert.Null(ConnectionRequests.GetRequestedName(null, []));
    }
}
