// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using NCode.Buffers;
using NCode.Encoders;

namespace NCode.Identity.Bff.Sessions;

/// <summary>
/// Builds the keys that the default session store uses in the shared-state sets.
/// </summary>
internal static class BffStoreKeys
{
    /// <summary>
    /// Gets the key of a session value.
    /// </summary>
    public static string Session(string storeKey) => "bff:session:" + storeKey;

    /// <summary>
    /// Gets the key of a login transaction.
    /// </summary>
    public static string Transaction(string transactionKey) => "bff:txn:" + transactionKey;

    /// <summary>
    /// Gets the key of a refresh lease.
    /// </summary>
    public static string Lease(string leaseKey) => "bff:lease:" + leaseKey;

    /// <summary>
    /// Gets the key of the index of the sessions of an OP session identifier.
    /// </summary>
    public static string SessionIdIndex(string issuer, string sessionId) =>
        "bff:sid:" + Digest(issuer, sessionId);

    /// <summary>
    /// Gets the key of the index of the sessions of a subject.
    /// </summary>
    public static string SubjectIndex(string issuer, string subject) =>
        "bff:sub:" + Digest(issuer, subject);

    /// <summary>
    /// Gets the key of the index whose members list the clients that have sessions.
    /// </summary>
    public const string ClientsIndex = "bff:clients";

    /// <summary>
    /// Gets the key of the index of the sessions of a client.
    /// </summary>
    public static string ClientIndex(BffSessionClient client) => ClientIndex(ClientDigest(client));

    /// <summary>
    /// Gets the key of the index of the sessions of the client that a digest names.
    /// </summary>
    public static string ClientIndex(string clientDigest) => "bff:client:" + clientDigest;

    /// <summary>
    /// Gets the digest that names a client in the keys and in the members of <see cref="ClientsIndex"/>, so that the
    /// shared state never holds a client's frontend, authority or client identifier in the clear.
    /// </summary>
    public static string ClientDigest(BffSessionClient client) =>
        Digest(client.Frontend, Digest(client.Authority, client.ClientId));

    /// <summary>
    /// Gets the instant at which the member of <see cref="ClientsIndex"/> for a session's absolute expiry expires:
    /// the end of that expiry's UTC day.
    /// </summary>
    // See ADR-1443.
    public static DateTimeOffset ClientMemberExpiresAt(DateTimeOffset absoluteExpiresAt) =>
        new DateTimeOffset(absoluteExpiresAt.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);

    /// <summary>
    /// The format of the UTC day segment of a client's session index member.
    /// </summary>
    private const string ClientMemberDayFormat = "yyyy-MM-dd";

    /// <summary>
    /// The character that separates a client member's day from its client digest.
    /// </summary>
    private const char ClientMemberSeparator = ':';

    /// <summary>
    /// Gets the member of <see cref="ClientsIndex"/> that lists a client for the UTC day of a session's absolute
    /// expiry: the day, a colon and the client's digest.
    /// </summary>
    /// <param name="client">The client that the session was issued to.</param>
    /// <param name="absoluteExpiresAt">The absolute expiry of the session.</param>
    /// <returns>The member.</returns>
    public static string ClientMember(BffSessionClient client, DateTimeOffset absoluteExpiresAt) =>
        absoluteExpiresAt.UtcDateTime.ToString(ClientMemberDayFormat, CultureInfo.InvariantCulture)
        + ClientMemberSeparator
        + ClientDigest(client);

    /// <summary>
    /// Reads the client digest of a member of <see cref="ClientsIndex"/>.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <param name="digest">The client digest that the member names.</param>
    /// <returns><see langword="true"/> when the member is well formed; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseClientMember(string member, out string digest)
    {
        digest = string.Empty;

        var separator = member.IndexOf(ClientMemberSeparator);
        if (
            separator != ClientMemberDayFormat.Length
            || separator == member.Length - 1
            || !DateOnly.TryParseExact(
                member.AsSpan(0, separator),
                ClientMemberDayFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _
            )
        )
            return false;

        digest = member[(separator + 1)..];
        return true;
    }

    // Each value is length-prefixed, so a value cannot make a key ambiguous or unbounded.
    private static string Digest(string first, string second)
    {
        var firstLength = SecureEncoding.UTF8.GetByteCount(first);
        var secondLength = SecureEncoding.UTF8.GetByteCount(second);

        var secondOffset = sizeof(int) + firstLength;
        using var lease = BufferFactory.Rent(
            secondOffset + sizeof(int) + secondLength,
            isSensitive: false,
            out Span<byte> buffer
        );
        BinaryPrimitives.WriteInt32BigEndian(buffer[..sizeof(int)], firstLength);
        SecureEncoding.UTF8.GetBytes(first, buffer.Slice(sizeof(int), firstLength));
        BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(secondOffset, sizeof(int)), secondLength);
        SecureEncoding.UTF8.GetBytes(
            second,
            buffer.Slice(secondOffset + sizeof(int), secondLength)
        );

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(buffer, hash);
        return Base64Url.Encode(hash);
    }
}
