// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class ReplicatedDocumentTests
{
    [Fact]
    public void Serialize_WhenAuthorityContent_RoundTripsThroughTheEnvelope()
    {
        var content = new PlatformAuthorityDocumentContent
        {
            Assignments =
            [
                new AuthorityAssignment { PrincipalId = "p1", RoleName = "GlobalAdmin" },
            ],
            Clients = [new AuthorityClient { PrincipalId = "p2", ClientId = "console" }],
        };
        var document = new ReplicatedDocument
        {
            Kind = ReplicatedDocumentKinds.PlatformAuthority,
            Revision = 3,
            Content = JsonSerializer.SerializeToElement(content, DirectoryJson.Options),
        };

        var json = JsonSerializer.Serialize(document, DirectoryJson.Options);
        var roundTripped = JsonSerializer.Deserialize<ReplicatedDocument>(
            json,
            DirectoryJson.Options
        );

        Assert.NotNull(roundTripped);
        Assert.Equal(ReplicatedDocumentKinds.PlatformAuthority, roundTripped.Kind);
        Assert.Equal("", roundTripped.Key);
        Assert.Equal(3, roundTripped.Revision);
        Assert.NotNull(roundTripped.Content);
        var result = roundTripped.Content.Value.Deserialize<PlatformAuthorityDocumentContent>(
            DirectoryJson.Options
        );
        Assert.NotNull(result);
        Assert.Equal("p1", Assert.Single(result.Assignments).PrincipalId);
        Assert.Equal("console", Assert.Single(result.Clients).ClientId);
        Assert.Contains("\"assignments\"", json);
        Assert.Contains("\"roleName\"", json);
    }

    [Fact]
    public void Deserialize_WhenPlatformAuthorityLacksClients_Throws()
    {
        const string json = """{"assignments":[]}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<PlatformAuthorityDocumentContent>(
                json,
                DirectoryJson.Options
            )
        );
    }

    [Fact]
    public void Serialize_WhenTeamAuthorityContent_CarriesNoClients()
    {
        var content = new TeamAuthorityDocumentContent
        {
            Assignments = [new AuthorityAssignment { PrincipalId = "p1", RoleName = "TeamAdmin" }],
        };

        var json = JsonSerializer.Serialize(content, DirectoryJson.Options);

        Assert.Contains("\"assignments\"", json);
        Assert.DoesNotContain("clients", json);
    }

    [Fact]
    public void Serialize_WhenManagementKeysContent_RoundTripsKeysAndRevokedIds()
    {
        var content = new ManagementKeysDocumentContent
        {
            Keys =
            [
                new ManagementKeyEntry
                {
                    KeyId = "k1",
                    SecretType = "rsa",
                    Algorithm = "PS256",
                    Use = "sig",
                    KeySizeBits = 2048,
                    PublicKey = "AQAB",
                    ActivatesWhen = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    RetiresWhen = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    ExpiresWhen = new DateTimeOffset(2032, 1, 1, 0, 0, 0, TimeSpan.Zero),
                },
            ],
            RevokedKeyIds = ["k0"],
        };

        var json = JsonSerializer.Serialize(content, DirectoryJson.Options);
        var result = JsonSerializer.Deserialize<ManagementKeysDocumentContent>(
            json,
            DirectoryJson.Options
        );

        Assert.NotNull(result);
        var key = Assert.Single(result.Keys);
        Assert.Equal("k1", key.KeyId);
        Assert.Equal(2048, key.KeySizeBits);
        Assert.Equal(content.Keys[0].ActivatesWhen, key.ActivatesWhen);
        Assert.Equal(content.Keys[0].RetiresWhen, key.RetiresWhen);
        Assert.Equal(content.Keys[0].ExpiresWhen, key.ExpiresWhen);
        Assert.Equal("k0", Assert.Single(result.RevokedKeyIds));
        Assert.Contains("\"kid\"", json);
    }

    [Fact]
    public void Deserialize_WhenManagementKeyLacksExpiresWhen_Throws()
    {
        const string json = """
            {"keys":[{"kid":"k1","secretType":"rsa","algorithm":"PS256","use":"sig","keySizeBits":2048,
            "publicKey":"AQAB","activatesWhen":"2030-01-01T00:00:00+00:00","retiresWhen":"2031-01-01T00:00:00+00:00"}],
            "revokedKeyIds":[]}
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ManagementKeysDocumentContent>(json, DirectoryJson.Options)
        );
    }

    [Fact]
    public void Deserialize_WhenManagementKeysLackRevokedKeyIds_Throws()
    {
        const string json = """
            {"keys":[{"kid":"k1","secretType":"rsa","algorithm":"PS256","use":"sig","keySizeBits":2048,
            "publicKey":"AQAB","activatesWhen":"2030-01-01T00:00:00+00:00","retiresWhen":"2031-01-01T00:00:00+00:00",
            "expiresWhen":"2032-01-01T00:00:00+00:00"}]}
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ManagementKeysDocumentContent>(json, DirectoryJson.Options)
        );
    }

    [Fact]
    public void Serialize_WhenChangeCarriesAReplicatedDocument_RoundTripsIt()
    {
        var change = new DirectoryChange
        {
            Sequence = 7,
            Replicated = new ReplicatedDocument
            {
                Kind = ReplicatedDocumentKinds.TeamAuthority,
                Key = "team-a",
                Revision = 2,
            },
        };

        var json = JsonSerializer.Serialize(change, DirectoryJson.Options);
        var result = JsonSerializer.Deserialize<DirectoryChange>(json, DirectoryJson.Options);

        Assert.NotNull(result);
        Assert.NotNull(result.Replicated);
        Assert.Equal("team-a", result.Replicated.Key);
        Assert.Null(result.Replicated.Content);
    }
}
