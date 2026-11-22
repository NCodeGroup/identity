// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using NCode.Buffers;

namespace NCode.Identity.OpenId.Authentication.Auditing.Streams;

/// <summary>
/// Signs the delivery of a batch of audit records as the Standard Webhooks specification does: an HMAC-SHA256 over the
/// message identifier, the timestamp and the body, so a receiver verifies the sender and rejects a replayed message.
/// </summary>
// See ADR-1534.
internal static class AuditStreamSignature
{
    /// <summary>
    /// The prefix of a signature that names the scheme.
    /// </summary>
    internal const string Version = "v1";

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

        var timestampText = timestamp.ToString(CultureInfo.InvariantCulture);
        var prefix = SecureEncoding.UTF8.GetBytes($"{messageId}.{timestampText}.");

        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, secret);
        hmac.AppendData(prefix);
        hmac.AppendData(body);
        var hash = hmac.GetHashAndReset();

        return $"{Version},{Convert.ToBase64String(hash)}";
    }
}
