// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class EmailCodeHashJsonConverterTests : BaseTests
{
    private static DateTimeOffset ExpiresWhen { get; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static EmailCodeHash CreateHash()
    {
        var hash = default(EmailCodeHash);
        Span<byte> bytes = hash;
        for (var index = 0; index < bytes.Length; ++index)
        {
            bytes[index] = (byte)(index + 1);
        }

        return hash;
    }

    private static string StateJson(string codeHashJson) =>
        $$"""{"connection_id":"c","local_account_id":"a","email":"e","code_hash":{{codeHashJson}},"attempts":0,"expires_when":"2026-11-22T12:00:00+00:00"}""";

    private static EmailCodeState Deserialize(string json) =>
        JsonSerializer.Deserialize(
            SecureEncoding.UTF8.GetBytes(json),
            EmailCodeJsonContext.Default.EmailCodeState
        )!;

    #region Write

    [Fact]
    public void Write_Always_WritesTheHashAsBase64()
    {
        var hash = CreateHash();
        var state = new EmailCodeState("c", "a", "e", hash, Attempts: 0, ExpiresWhen);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            state,
            EmailCodeJsonContext.Default.EmailCodeState
        );

        var text = JsonElement.Parse(bytes).GetProperty("code_hash").GetString();
        ReadOnlySpan<byte> expected = hash;
        Assert.Equal(Convert.ToBase64String(expected), text);

        var roundTripped = JsonSerializer.Deserialize(
            bytes,
            EmailCodeJsonContext.Default.EmailCodeState
        )!;
        var roundTrippedHash = roundTripped.CodeHash;
        ReadOnlySpan<byte> actual = roundTrippedHash;
        Assert.True(actual.SequenceEqual(expected));
    }

    #endregion

    #region Read

    [Fact]
    public void Read_AValueOfAnotherLength_Throws()
    {
        var json = StateJson($"\"{Convert.ToBase64String(new byte[16])}\"");

        Assert.Throws<JsonException>(() => Deserialize(json));
    }

    [Fact]
    public void Read_ANumber_Throws()
    {
        var json = StateJson("1");

        Assert.Throws<JsonException>(() => Deserialize(json));
    }

    #endregion
}
