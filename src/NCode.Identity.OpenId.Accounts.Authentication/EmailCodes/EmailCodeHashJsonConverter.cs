// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Reads and writes an <see cref="EmailCodeHash"/> as a base64 JSON string, without an array.
/// </summary>
// See ADR-1533.
internal sealed class EmailCodeHashJsonConverter : JsonConverter<EmailCodeHash>
{
    /// <inheritdoc />
    public override EmailCodeHash Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (
            reader.TokenType != JsonTokenType.String
            || reader.HasValueSequence
            || reader.ValueIsEscaped
        )
        {
            throw new JsonException("A code hash is a base64 string.");
        }

        var hash = default(EmailCodeHash);
        Span<byte> destination = hash;
        var status = Base64.DecodeFromUtf8(reader.ValueSpan, destination, out _, out var written);
        if (status != OperationStatus.Done || written != EmailCodeHash.Length)
        {
            throw new JsonException("A code hash is 32 bytes of base64.");
        }

        return hash;
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        EmailCodeHash value,
        JsonSerializerOptions options
    )
    {
        ReadOnlySpan<byte> bytes = value;
        writer.WriteBase64StringValue(bytes);
    }
}
