// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Buffers;
using NCode.Identity.Bff.Serialization;

namespace NCode.Identity.Bff.Sessions;

/// <summary>
/// Reads and writes the plaintext client that the session store holds for a member of the clients index.
/// </summary>
internal static class BffSessionClientSerializer
{
    /// <summary>
    /// Writes a client as UTF-8 JSON into pooled memory.
    /// </summary>
    /// <param name="client">The client to write.</param>
    /// <param name="value">The JSON bytes, which are valid until the returned rental is disposed.</param>
    /// <returns>
    /// The rental that owns <paramref name="value"/>; dispose it after the store has taken the value.
    /// </returns>
    public static IDisposable Serialize(BffSessionClient client, out ReadOnlyMemory<byte> value)
    {
        using var buffer = BufferFactory.CreatePooledBufferWriter(isSensitive: false);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, client, BffJsonContext.Default.BffSessionClient);
            writer.Flush();
        }

        using var lease = buffer.GetSpanLease(isSensitive: false);
        var rental = BufferFactory.Rent(
            lease.Span.Length,
            isSensitive: false,
            out Memory<byte> memory
        );

        try
        {
            lease.Span.CopyTo(memory.Span);
        }
        catch
        {
            rental.Dispose();
            throw;
        }

        value = memory;
        return rental;
    }

    /// <summary>
    /// Reads a client, returning <see langword="null"/> when the bytes are not a client.
    /// </summary>
    /// <param name="value">The stored JSON bytes.</param>
    /// <returns>The client; otherwise, <see langword="null"/>.</returns>
    public static BffSessionClient? Deserialize(ReadOnlySpan<byte> value)
    {
        BffSessionClient client;
        try
        {
            client = JsonSerializer.Deserialize(value, BffJsonContext.Default.BffSessionClient);
        }
        catch (JsonException)
        {
            return null;
        }

        // A document that omits a member, or holds a JSON null, leaves the member null.
        if (client.Frontend is null || client.Authority is null || client.ClientId is null)
            return null;

        return client;
    }
}
