// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using NCode.Buffers;
using NCode.Identity.Bff.Serialization;

namespace NCode.Identity.Bff.Sessions;

/// <summary>
/// Encrypts and decrypts the payload of a session record under a key derived from the session handle, and binds it to
/// the record's store key, version and plaintext metadata.
/// </summary>
internal static class BffSessionCodec
{
    /// <summary>
    /// Derives the store key of a session from its handle.
    /// </summary>
    public static bool TryGetStoreKey(string? handle, [NotNullWhen(true)] out string? storeKey) =>
        BffKeyDerivation.TryDeriveIdentifier(
            handle,
            BffKeyDerivation.SessionIdPurpose,
            out storeKey
        );

    /// <summary>
    /// Builds the record that the store holds for a session.
    /// </summary>
    /// <exception cref="ArgumentException">The handle is not valid.</exception>
    public static BffSessionRecord Encode(
        string handle,
        long version,
        BffSessionMetadata metadata,
        BffSessionPayload payload
    )
    {
        if (!TryGetStoreKey(handle, out var storeKey))
            throw new ArgumentException("The handle is not valid.", nameof(handle));

        using var buffer = BufferFactory.CreateContiguousBufferWriter(isSensitive: true);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, payload, BffJsonContext.Default.BffSessionPayload);
            writer.Flush();
        }

        using var associatedData = BufferFactory.CreateContiguousBufferWriter(isSensitive: false);
        WriteAssociatedData(storeKey, version, metadata, associatedData);

        // The record owns its protected payload beyond this call, so it is an exactly sized array.
        var protectedPayload = new byte[
            BffPayloadProtector.GetProtectedLength(buffer.WrittenCount)
        ];
        BffPayloadProtector.Protect(
            handle,
            BffKeyDerivation.SessionPayloadPurpose,
            buffer.WrittenSpan,
            associatedData.WrittenSpan,
            protectedPayload
        );

        return new BffSessionRecord(storeKey, version, metadata, protectedPayload);
    }

    /// <summary>
    /// Decrypts the payload of a session record, returning <c>false</c> when it does not authenticate.
    /// </summary>
    public static bool TryDecode(
        string handle,
        BffSessionRecord record,
        [NotNullWhen(true)] out BffSessionPayload? payload
    )
    {
        payload = null;

        using var associatedData = BufferFactory.CreateContiguousBufferWriter(isSensitive: false);
        WriteAssociatedData(record.StoreKey, record.Version, record.Metadata, associatedData);

        using var buffer = BufferFactory.CreateContiguousBufferWriter(isSensitive: true);
        if (
            !BffPayloadProtector.TryUnprotect(
                handle,
                BffKeyDerivation.SessionPayloadPurpose,
                record.ProtectedPayload.Span,
                associatedData.WrittenSpan,
                buffer
            )
        )
            return false;

        try
        {
            payload = JsonSerializer.Deserialize(
                buffer.WrittenSpan,
                BffJsonContext.Default.BffSessionPayload
            );
        }
        catch (JsonException)
        {
            return false;
        }

        return payload is not null;
    }

    /// <summary>
    /// Writes the associated data, which binds the payload to the store key, the version and the plaintext metadata.
    /// </summary>
    /// <param name="storeKey">The store key of the session.</param>
    /// <param name="version">The version of the record.</param>
    /// <param name="metadata">The plaintext metadata of the record.</param>
    /// <param name="destination">The buffer that receives the associated data.</param>
    private static void WriteAssociatedData<TWriter>(
        string storeKey,
        long version,
        BffSessionMetadata metadata,
        TWriter destination
    )
        where TWriter : IBufferWriter<byte>
    {
        _ = SecureEncoding.UTF8.GetBytes(storeKey, destination);

        var span = destination.GetSpan(sizeof(long));
        BinaryPrimitives.WriteInt64BigEndian(span, version);
        destination.Advance(sizeof(long));

        BffSessionRecordSerializer.WriteMetadata(metadata, destination);
    }
}
