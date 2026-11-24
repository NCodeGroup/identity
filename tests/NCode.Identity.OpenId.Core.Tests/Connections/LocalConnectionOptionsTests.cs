// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Connections;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Connections;

public class LocalConnectionOptionsTests
{
    [Fact]
    public void Deserialize_WhenDocumentHasAnUnknownMember_KeepsItInExtensionData()
    {
        var options = JsonSerializer.Deserialize<LocalConnectionOptions>(
            """{"store":"default","custom":{"a":1}}"""
        );

        Assert.NotNull(options);
        Assert.Equal("default", options.Store);
        Assert.NotNull(options.ExtensionData);
        Assert.Equal(1, options.ExtensionData["custom"].GetProperty("a").GetInt32());
    }

    [Fact]
    public void Serialize_WhenOptionsHaveExtensionData_WritesTheUnknownMemberBack()
    {
        var options = JsonSerializer.Deserialize<LocalConnectionOptions>(
            """{"store":"default","custom":"x"}"""
        );

        var json = JsonSerializer.Serialize(options);

        var root = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("default", root.GetProperty("store").GetString());
        Assert.Equal("x", root.GetProperty("custom").GetString());
    }
}
