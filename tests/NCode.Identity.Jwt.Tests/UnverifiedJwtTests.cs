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
}
