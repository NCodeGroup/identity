// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Authentication.Sessions;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Sessions;

public class SessionGrantSerializationTests
{
    private static IPersistedPayloadSerializer Serializer { get; } =
        new ServiceCollection()
            .AddPersistedPayloadSerializer()
            .BuildServiceProvider()
            .GetRequiredService<IPersistedPayloadSerializer>();

    private JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    #region Deserialize Tests

    [Theory]
    [InlineData("idleTimeout")]
    [InlineData("absoluteExpiresWhen")]
    public void Deserialize_WhenSessionLacksABound_Throws(string missingMember)
    {
        var payload = new JsonObject
        {
            ["authTime"] = "2026-01-01T00:00:00+00:00",
            ["clientIds"] = new JsonArray("client-1"),
            ["lastActivityWhen"] = "2026-01-01T00:00:00+00:00",
            ["idleTimeout"] = "01:00:00",
            ["absoluteExpiresWhen"] = "2026-01-31T00:00:00+00:00",
        };
        payload.Remove(missingMember);
        var stored = JsonSerializer.SerializeToElement(
            new JsonObject { ["$ver"] = 1, ["$payload"] = payload }
        );

        Assert.Throws<JsonException>(() =>
            Serializer.Deserialize<SessionGrant>(stored, JsonOptions)
        );
    }

    [Fact]
    public void Deserialize_WhenStructPayloadHasUnknownMember_PreservesItThroughRewrite()
    {
        var stored = ParseDocument(
            """
            {
                "$ver": 1,
                "$payload": {
                    "authTime": "2026-01-01T00:00:00+00:00",
                    "clientIds": ["client-1"],
                    "lastActivityWhen": "2026-01-01T00:00:00+00:00",
                    "idleTimeout": "01:00:00",
                    "absoluteExpiresWhen": "2026-01-31T00:00:00+00:00",
                    "futureMember": "keep-me"
                }
            }
            """
        );

        var session = Serializer.Deserialize<SessionGrant>(stored, JsonOptions);

        Assert.NotNull(session.ExtensionData);
        Assert.True(session.ExtensionData!.ContainsKey("futureMember"));

        var rewritten = Serializer.Serialize(session, JsonOptions);
        Assert.Equal(
            "keep-me",
            rewritten.GetProperty("$payload").GetProperty("futureMember").GetString()
        );
    }

    #endregion

    private static JsonElement ParseDocument(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);
}
