// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Buffers;
using NCode.Identity.SharedState;

namespace NCode.Identity.Bff.Sessions;

/// <summary>
/// The default <see cref="IBffSessionStore"/>, which keeps sessions, login transactions and refresh leases in the
/// shared-state sets that every replica sees.
/// </summary>
internal class DefaultBffSessionStore(IExpiringValueSet values, IExpiringIndexSet indexes)
    : IBffSessionStore
{
    /// <summary>
    /// The format of the UTC day segment of a client's session index member.
    /// </summary>
    private const string ClientMemberDayFormat = "yyyy-MM-dd";

    /// <summary>
    /// The character that separates a client member's day from its client digest.
    /// </summary>
    private const char ClientMemberSeparator = ':';

    /// <summary>
    /// Encodes a refresh lease's owner token as UTF-8 into sensitive pooled memory that is zeroed when the returned
    /// lease is disposed. The bytes live in <see cref="ReadOnlyMemory{T}"/> because they cross an <c>await</c>.
    /// </summary>
    /// <param name="owner">The owner token to encode.</param>
    /// <param name="bytes">The owner token's UTF-8 bytes.</param>
    /// <returns>The lease that returns the memory to the pool.</returns>
    private static IDisposable RentOwnerBytes(string owner, out ReadOnlyMemory<byte> bytes)
    {
        var byteCount = SecureEncoding.UTF8.GetByteCount(owner);
        var lease = BufferFactory.Rent(byteCount, isSensitive: true, out Memory<byte> buffer);
        SecureEncoding.UTF8.GetBytes(owner, buffer.Span);
        bytes = buffer;
        return lease;
    }

    private static DateTimeOffset GetExpiresWhen(BffSessionMetadata metadata) =>
        metadata.IdleExpiresAt < metadata.AbsoluteExpiresAt
            ? metadata.IdleExpiresAt
            : metadata.AbsoluteExpiresAt;

    /// <summary>
    /// Gets the member of <see cref="BffStoreKeys.ClientsIndex"/> that lists a client for the UTC day of a session's
    /// absolute expiry: the day, a colon and the client's digest.
    /// </summary>
    /// <param name="client">The client that the session was issued to.</param>
    /// <param name="absoluteExpiresAt">The absolute expiry of the session.</param>
    /// <returns>The member.</returns>
    private static string GetClientMember(
        BffSessionClient client,
        DateTimeOffset absoluteExpiresAt
    ) =>
        absoluteExpiresAt.UtcDateTime.ToString(ClientMemberDayFormat, CultureInfo.InvariantCulture)
        + ClientMemberSeparator
        + BffStoreKeys.ClientDigest(client);

    /// <summary>
    /// Reads the client digest of a member of <see cref="BffStoreKeys.ClientsIndex"/>.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <param name="digest">The client digest that the member names.</param>
    /// <returns><see langword="true"/> when the member is well formed; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetClientDigest(string member, out string digest)
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

    private static bool IsStoreFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is not BffStoreUnavailableException
        && (
            exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested
        );

    private static BffStoreUnavailableException Unavailable(Exception exception) =>
        new("The BFF session store is unavailable.", exception);

    /// <inheritdoc />
    public async ValueTask<bool> TryCreateAsync(
        BffSessionRecord record,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var metadata = record.Metadata;
            var key = BffStoreKeys.Session(record.StoreKey);
            using var lease = BffSessionRecordSerializer.Serialize(
                metadata,
                record.ProtectedPayload.Span,
                out var value
            );
            var expiresWhen = GetExpiresWhen(metadata);

            var added = await values.TryAddAsync(key, value, expiresWhen, cancellationToken);
            if (!added)
                return false;

            if (metadata.SessionId is { Length: > 0 } sessionId)
            {
                var sessionIdIndex = BffStoreKeys.SessionIdIndex(metadata.Issuer, sessionId);
                await indexes.AddMemberAsync(
                    sessionIdIndex,
                    record.StoreKey,
                    metadata.AbsoluteExpiresAt,
                    cancellationToken
                );
            }

            var subjectIndex = BffStoreKeys.SubjectIndex(metadata.Issuer, metadata.Subject);
            await indexes.AddMemberAsync(
                subjectIndex,
                record.StoreKey,
                metadata.AbsoluteExpiresAt,
                cancellationToken
            );

            var client = metadata.Client;
            await indexes.AddMemberAsync(
                BffStoreKeys.ClientIndex(client),
                record.StoreKey,
                metadata.AbsoluteExpiresAt,
                cancellationToken
            );
            await indexes.AddMemberAsync(
                BffStoreKeys.ClientsIndex,
                GetClientMember(client, metadata.AbsoluteExpiresAt),
                BffStoreKeys.ClientMemberExpiresAt(metadata.AbsoluteExpiresAt),
                cancellationToken
            );

            return true;
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<BffSessionRecord?> GetAsync(
        string storeKey,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var key = BffStoreKeys.Session(storeKey);
            var stored = await values.GetAsync(key, cancellationToken);
            if (stored is not { } found)
                return null;

            if (
                !BffSessionRecordSerializer.TryDeserialize(
                    found.Value,
                    out var metadata,
                    out var protectedPayload
                )
            )
                return null;

            return new BffSessionRecord(storeKey, found.Version, metadata, protectedPayload);
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryReplaceAsync(
        BffSessionRecord record,
        long expectedVersion,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var metadata = record.Metadata;
            var key = BffStoreKeys.Session(record.StoreKey);
            using var lease = BffSessionRecordSerializer.Serialize(
                metadata,
                record.ProtectedPayload.Span,
                out var value
            );
            var expiresWhen = GetExpiresWhen(metadata);

            return await values.TryReplaceAsync(
                key,
                expectedVersion,
                value,
                expiresWhen,
                cancellationToken
            );
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> RemoveAsync(
        string storeKey,
        BffSessionMetadata metadata,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var key = BffStoreKeys.Session(storeKey);
            var removed = await values.TryTakeAsync(key, cancellationToken) is not null;

            if (metadata.SessionId is { Length: > 0 } sessionId)
            {
                var sessionIdIndex = BffStoreKeys.SessionIdIndex(metadata.Issuer, sessionId);
                await indexes.RemoveMemberAsync(sessionIdIndex, storeKey, cancellationToken);
            }

            var subjectIndex = BffStoreKeys.SubjectIndex(metadata.Issuer, metadata.Subject);
            await indexes.RemoveMemberAsync(subjectIndex, storeKey, cancellationToken);

            var client = metadata.Client;
            await indexes.RemoveMemberAsync(
                BffStoreKeys.ClientIndex(client),
                storeKey,
                cancellationToken
            );

            return removed;
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> FindBySessionIdAsync(
        string issuer,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var indexKey = BffStoreKeys.SessionIdIndex(issuer, sessionId);
            return await indexes.GetMembersAsync(indexKey, cancellationToken);
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> FindBySubjectAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var indexKey = BffStoreKeys.SubjectIndex(issuer, subject);
            return await indexes.GetMembersAsync(indexKey, cancellationToken);
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<BffSessionClient>> FindClientsAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            var members = await indexes.GetMembersAsync(
                BffStoreKeys.ClientsIndex,
                cancellationToken
            );

            var clients = new List<BffSessionClient>();
            var digests = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                if (!TryGetClientDigest(member, out var digest) || !digests.Add(digest))
                    continue;

                var indexKey = BffStoreKeys.ClientIndex(digest);
                var storeKeys = await indexes.GetMembersAsync(indexKey, cancellationToken);
                if (
                    await TryResolveClientAsync(storeKeys, cancellationToken) is { } client
                    && BffStoreKeys.ClientIndex(client) == indexKey
                )
                    clients.Add(client);
            }

            return clients;
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> FindByClientAsync(
        BffSessionClient client,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var indexKey = BffStoreKeys.ClientIndex(client);
            return await indexes.GetMembersAsync(indexKey, cancellationToken);
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryAddTransactionAsync(
        string transactionKey,
        ReadOnlyMemory<byte> protectedTransaction,
        DateTimeOffset expiresWhen,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var key = BffStoreKeys.Transaction(transactionKey);
            return await values.TryAddAsync(
                key,
                protectedTransaction,
                expiresWhen,
                cancellationToken
            );
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>?> TryTakeTransactionAsync(
        string transactionKey,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var key = BffStoreKeys.Transaction(transactionKey);
            var taken = await values.TryTakeAsync(key, cancellationToken);
            if (taken is not { } found)
                return null;

            return found.Value;
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<BffRefreshLease?> TryAcquireRefreshLeaseAsync(
        string leaseKey,
        DateTimeOffset expiresWhen,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var key = BffStoreKeys.Lease(leaseKey);
            var owner = BffKeyDerivation.CreateSecret();
            using var lease = RentOwnerBytes(owner, out var value);
            var added = await values.TryAddAsync(key, value, expiresWhen, cancellationToken);
            return added ? new BffRefreshLease(leaseKey, owner) : null;
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsRefreshLeaseHeldAsync(
        string leaseKey,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var key = BffStoreKeys.Lease(leaseKey);
            var stored = await values.GetAsync(key, cancellationToken);
            return stored is not null;
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseRefreshLeaseAsync(
        BffRefreshLease lease,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var key = BffStoreKeys.Lease(lease.LeaseKey);
            using var ownerLease = RentOwnerBytes(lease.Owner, out var owner);
            await values.TryRemoveAsync(key, owner, cancellationToken);
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }
    }

    /// <summary>
    /// Reads the client from the first session record that can be read among the members of a client's index.
    /// </summary>
    /// <param name="storeKeys">The store keys of the sessions in the client's index.</param>
    /// <param name="cancellationToken">A token that cancels the reads.</param>
    /// <returns>The client of the first readable record; otherwise, <see langword="null"/>.</returns>
    private async ValueTask<BffSessionClient?> TryResolveClientAsync(
        IReadOnlyList<string> storeKeys,
        CancellationToken cancellationToken
    )
    {
        foreach (var storeKey in storeKeys)
        {
            if (await GetAsync(storeKey, cancellationToken) is { } record)
                return record.Metadata.Client;
        }

        return null;
    }
}
