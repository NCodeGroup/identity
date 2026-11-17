// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Security.Cryptography;
using NCode.Buffers;
using NCode.Encoders;
using NCode.Identity.Bff.Sessions;
using Xunit;

namespace NCode.Identity.Bff.Tests.Sessions;

public class BffStoreKeysTests : BaseTests
{
    private static void AddLengthPrefixed(List<byte> layout, string value)
    {
        var bytes = SecureEncoding.UTF8.GetBytes(value);
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        layout.AddRange(length);
        layout.AddRange(bytes);
    }

    #region SubjectIndex Tests

    [Fact]
    public void SubjectIndex_IssuerAndSubject_IsLengthPrefixedSha256()
    {
        const string issuer = "https://issuer.example";
        const string subject = "sub-1";

        var layout = new List<byte>();
        AddLengthPrefixed(layout, issuer);
        AddLengthPrefixed(layout, subject);
        var expected = "bff:sub:" + Base64Url.Encode(SHA256.HashData(layout.ToArray()));

        var result = BffStoreKeys.SubjectIndex(issuer, subject);

        Assert.Equal(expected, result);
    }

    #endregion

    #region ClientIndex Tests

    [Fact]
    public void ClientIndex_SameClient_IsStable()
    {
        var first = BffStoreKeys.ClientIndex(
            new BffSessionClient("web", "https://op.example", "client-1")
        );
        var second = BffStoreKeys.ClientIndex(
            new BffSessionClient("web", "https://op.example", "client-1")
        );

        Assert.Equal(first, second);
        Assert.StartsWith("bff:client:", first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other", "https://op.example", "client-1")]
    [InlineData("web", "https://other.example", "client-1")]
    [InlineData("web", "https://op.example", "client-2")]
    public void ClientIndex_AnyPartDiffers_Differs(
        string frontend,
        string authority,
        string clientId
    )
    {
        var baseline = BffStoreKeys.ClientIndex(
            new BffSessionClient("web", "https://op.example", "client-1")
        );

        var result = BffStoreKeys.ClientIndex(new BffSessionClient(frontend, authority, clientId));

        Assert.NotEqual(baseline, result);
    }

    #endregion

    #region ClientMemberExpiresAt Tests

    [Fact]
    public void ClientMemberExpiresAt_Always_IsTheEndOfTheUtcDay()
    {
        var absoluteExpiresAt = new DateTimeOffset(2030, 5, 6, 23, 59, 59, TimeSpan.FromHours(-7));

        var result = BffStoreKeys.ClientMemberExpiresAt(absoluteExpiresAt);

        Assert.Equal(new DateTimeOffset(2030, 5, 8, 0, 0, 0, TimeSpan.Zero), result);
    }

    #endregion

    #region ClientMember Tests

    [Fact]
    public void ClientMember_ThenTryParseClientMember_RoundTripsTheDigest()
    {
        var client = new BffSessionClient("web", "https://op.example", "client-1");
        var absoluteExpiresAt = new DateTimeOffset(2030, 5, 6, 23, 59, 59, TimeSpan.FromHours(-7));

        var member = BffStoreKeys.ClientMember(client, absoluteExpiresAt);
        var parsed = BffStoreKeys.TryParseClientMember(member, out var digest);

        Assert.True(parsed);
        Assert.Equal(BffStoreKeys.ClientDigest(client), digest);
        Assert.StartsWith("2030-05-07:", member, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2030-13-40:digest")]
    [InlineData("not-a-day:digest")]
    [InlineData("digest")]
    public void TryParseClientMember_WhenTheDayIsMalformed_ReturnsFalse(string member)
    {
        var parsed = BffStoreKeys.TryParseClientMember(member, out var digest);

        Assert.False(parsed);
        Assert.Empty(digest);
    }

    [Fact]
    public void TryParseClientMember_WhenTheDigestIsEmpty_ReturnsFalse()
    {
        var parsed = BffStoreKeys.TryParseClientMember("2030-05-07:", out var digest);

        Assert.False(parsed);
        Assert.Empty(digest);
    }

    #endregion

    #region ClientMemberValue Tests

    [Fact]
    public void ClientMemberValue_Always_PrefixesTheMember()
    {
        var client = new BffSessionClient("web", "https://op.example", "client-1");
        var member = BffStoreKeys.ClientMember(
            client,
            new DateTimeOffset(2030, 5, 6, 12, 0, 0, TimeSpan.Zero)
        );

        var result = BffStoreKeys.ClientMemberValue(member);

        Assert.Equal("bff:client-member:2030-05-06:" + BffStoreKeys.ClientDigest(client), result);
    }

    [Fact]
    public void ClientMemberValue_DifferentMembers_Differ()
    {
        var client = new BffSessionClient("web", "https://op.example", "client-1");
        var first = BffStoreKeys.ClientMember(
            client,
            new DateTimeOffset(2030, 5, 6, 12, 0, 0, TimeSpan.Zero)
        );
        var second = BffStoreKeys.ClientMember(
            client,
            new DateTimeOffset(2030, 5, 7, 12, 0, 0, TimeSpan.Zero)
        );

        Assert.NotEqual(
            BffStoreKeys.ClientMemberValue(first),
            BffStoreKeys.ClientMemberValue(second)
        );
    }

    #endregion

    #region SessionIdIndex Tests

    [Fact]
    public void SessionIdIndex_SwappedValues_Differ()
    {
        var first = BffStoreKeys.SessionIdIndex("ab", "c");
        var second = BffStoreKeys.SessionIdIndex("a", "bc");

        Assert.NotEqual(first, second);
    }

    #endregion
}
