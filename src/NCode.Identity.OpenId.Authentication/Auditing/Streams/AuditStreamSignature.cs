// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using NCode.Buffers;

namespace NCode.Identity.OpenId.Authentication.Auditing.Streams;

/// <summary>
/// Signs the delivery of a batch of audit records as the Standard Webhooks specification does: an HMAC-SHA256 over the
/// message identifier, the timestamp and the body, so a receiver verifies the sender and the body and, because the
/// identifier and the timestamp are signed, rejects a replayed message by the timestamp's age and by an identifier it
/// has seen.
/// </summary>
// See ADR-1534.
internal static class AuditStreamSignature
{
    /// <summary>
    /// The prefix of a signature that names the scheme.
    /// </summary>
    internal const string Version = "v1";

    /// <summary>
    /// The length in bytes of an HMAC-SHA256.
    /// </summary>
    private const int HashLength = 32;

    /// <summary>
    /// The length in characters of the longest decimal text of a <see cref="long"/>, such as
    /// <c>-9223372036854775808</c>.
    /// </summary>
    private const int MaxTimestampLength = 20;

    /// <summary>
    /// Computes the signature of a delivery.
    /// </summary>
    /// <param name="secret">The signing secret's bytes.</param>
    /// <param name="messageId">The identifier of the message, sent in the <c>webhook-id</c> header.</param>
    /// <param name="timestamp">The time of the delivery in Unix seconds, sent in the <c>webhook-timestamp</c>
    /// header.</param>
    /// <param name="body">The body of the delivery.</param>
    /// <returns>The version, a comma and the Base64 of the HMAC-SHA256 over <c>{messageId}.{timestamp}.</c> followed by
    /// the body, the value of one entry of the <c>webhook-signature</c> header.</returns>
    public static string Sign(
        ReadOnlySpan<byte> secret,
        string messageId,
        long timestamp,
        ReadOnlySpan<byte> body
    )
    {
        ArgumentNullException.ThrowIfNull(messageId);

        Span<byte> timestampBytes = stackalloc byte[MaxTimestampLength];
        if (
            !timestamp.TryFormat(
                timestampBytes,
                out var timestampLength,
                default,
                CultureInfo.InvariantCulture
            )
        )
            throw new InvalidOperationException("The timestamp does not fit its buffer.");

        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, secret);

        using var messageIdOwner = SecureEncoding.UTF8.RentBytes(
            messageId,
            isSensitive: false,
            out Span<byte> messageIdBytes
        );
        hmac.AppendData(messageIdBytes);
        hmac.AppendData("."u8);
        hmac.AppendData(timestampBytes[..timestampLength]);
        hmac.AppendData("."u8);
        hmac.AppendData(body);

        Span<byte> hash = stackalloc byte[HashLength];
        hmac.GetHashAndReset(hash);

        var signatureLength = Version.Length + 1 + Base64.GetMaxEncodedToUtf8Length(HashLength);
        return string.Create(
            signatureLength,
            (ReadOnlySpan<byte>)hash,
            static (chars, h) =>
            {
                Version.CopyTo(chars);
                chars[Version.Length] = ',';
                Convert.TryToBase64Chars(h, chars[(Version.Length + 1)..], out _);
            }
        );
    }
}
