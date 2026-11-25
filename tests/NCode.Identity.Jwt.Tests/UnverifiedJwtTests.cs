// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Jwt;

public class UnverifiedJwtTests
{
    private static string Encode(string json) =>
        System.Buffers.Text.Base64Url.EncodeToString(
            NCode.Buffers.SecureEncoding.UTF8.GetBytes(json)
        );

    private static string Compact(string payloadJson) =>
        $"{Encode("{\"alg\":\"none\"}")}.{Encode(payloadJson)}.sig";

    [Fact]
    public void TryReadIssuer_WhenThePayloadHasAnIssuer_ReturnsIt()
    {
        var result = UnverifiedJwt.TryReadIssuer(Compact("{\"iss\":\"cell-a\"}"), out var issuer);

        Assert.True(result);
        Assert.Equal("cell-a", issuer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("a.!!!.c")]
    public void TryReadIssuer_WhenTheShapeIsWrong_ReturnsFalse(string? token)
    {
        var result = UnverifiedJwt.TryReadIssuer(token, out var issuer);

        Assert.False(result);
        Assert.Empty(issuer);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"iss\":\"\"}")]
    [InlineData("{\"iss\":5}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void TryReadIssuer_WhenThePayloadHasNoUsableIssuer_ReturnsFalse(string payload)
    {
        var result = UnverifiedJwt.TryReadIssuer(Compact(payload), out var issuer);

        Assert.False(result);
        Assert.Empty(issuer);
    }

    private static string CompactWithHeader(string headerJson) =>
        $"{Encode(headerJson)}.{Encode("{}")}.sig";

    [Fact]
    public void TryReadKeyId_WhenTheHeaderHasAKeyId_ReturnsIt()
    {
        var result = UnverifiedJwt.TryReadKeyId(
            CompactWithHeader("{\"alg\":\"ES256\",\"kid\":\"key-1\"}"),
            out var keyId
        );

        Assert.True(result);
        Assert.Equal("key-1", keyId);
    }

    [Theory]
    [InlineData("{\"alg\":\"ES256\"}")]
    [InlineData("{\"kid\":1}")]
    [InlineData("{\"kid\":\"\"}")]
    [InlineData("[]")]
    public void TryReadKeyId_WhenTheHeaderHasNoStringKeyId_ReturnsFalse(string headerJson)
    {
        var result = UnverifiedJwt.TryReadKeyId(CompactWithHeader(headerJson), out var keyId);

        Assert.False(result);
        Assert.Empty(keyId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("!!!.b.c")]
    public void TryReadKeyId_WhenTheShapeIsWrong_ReturnsFalse(string? token)
    {
        var result = UnverifiedJwt.TryReadKeyId(token, out var keyId);

        Assert.False(result);
        Assert.Empty(keyId);
    }
}
