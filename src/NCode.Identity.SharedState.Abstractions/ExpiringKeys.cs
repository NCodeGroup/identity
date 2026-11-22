// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;
using NCode.Buffers;

namespace NCode.Identity.SharedState;

/// <summary>
/// Builds the keys a library records in the shared <see cref="IExpiringKeySet"/>. A key is
/// <c>{purpose}:{tenant in braces}:{digest}</c>, where the digest is the base64url SHA-256 of the length-prefixed UTF-8
/// components. The purpose and the tenant keep one tenant's records, and one kind of record, from colliding with
/// another on a store every tenant shares. The braces make the tenant the key's hash tag, so a clustered store keeps
/// one tenant's keys on one node and one script may touch several of them. The length prefixes keep distinct component
/// lists distinct — <c>("a:b", "c")</c> and <c>("a", "b:c")</c> never meet — and the digest bounds the key's length,
/// since the components come from the request.
/// </summary>
// See ADR-1246.
[PublicAPI]
public static class ExpiringKeys
{
    /// <summary>
    /// Builds the key for a record of the given purpose, scoped to a tenant, from its identifying components.
    /// </summary>
    /// <param name="purpose">The kind of record, which never contains a colon.</param>
    /// <param name="tenantId">
    /// The identifier of the tenant, or of the cell, that the record belongs to. It becomes the key's hash tag, so it
    /// holds no <c>{</c>, <c>}</c> or <c>:</c>.
    /// </param>
    /// <param name="components">The values that identify the record within its purpose and tenant.</param>
    /// <returns>The key to record in the shared expiring key set.</returns>
    public static string Create(
        string purpose,
        string tenantId,
        params ReadOnlySpan<string> components
    )
    {
        using var buffer = BufferFactory.CreateContiguousBufferWriter(isSensitive: false);

        foreach (var component in components)
        {
            var length = SecureEncoding.UTF8.GetByteCount(component);
            var lengthSpan = buffer.GetSpan(sizeof(int));
            BinaryPrimitives.WriteInt32BigEndian(lengthSpan, length);
            buffer.Advance(sizeof(int));
            SecureEncoding.UTF8.GetBytes(component, buffer);
        }

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(buffer.WrittenSpan, digest);

        var encodedDigest = Base64Url.EncodeToString(digest);

        return $"{purpose}:{{{tenantId}}}:{encodedDigest}";
    }
}
