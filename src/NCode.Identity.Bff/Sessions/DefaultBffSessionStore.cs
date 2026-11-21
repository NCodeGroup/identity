// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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
    /// Encodes a refresh lease's owner token as UTF-8 into sensitive pooled memory that is zeroed when the returned
    /// lease is disposed. The bytes live in <see cref="ReadOnlyMemory{T}"/> because they cross an <c>await</c>.
    /// </summary>
    /// <param name="owner">The owner token to encode.</param>
    /// <param name="bytes">The owner token's UTF-8 bytes.</param>
    /// <returns>The lease that returns the memory to the pool.</returns>
    private static IDisposable RentOwnerBytes(string owner, out ReadOnlyMemory<byte> bytes)
    {
        var lease = SecureEncoding.UTF8.RentBytes(
            owner,
            isSensitive: true,
            out Memory<byte> buffer
        );
        bytes = buffer;
        return lease;
    }

    private static DateTimeOffset GetExpiresWhen(BffSessionMetadata metadata) =>
        metadata.IdleExpiresAt < metadata.AbsoluteExpiresAt
            ? metadata.IdleExpiresAt
            : metadata.AbsoluteExpiresAt;

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
            var clientIndex = BffStoreKeys.ClientIndex(client);
            await indexes.AddMemberAsync(
                clientIndex,
                record.StoreKey,
                metadata.AbsoluteExpiresAt,
                cancellationToken
            );

            var member = BffStoreKeys.ClientMember(client, metadata.AbsoluteExpiresAt);
            var memberExpiresAt = BffStoreKeys.ClientMemberExpiresAt(metadata.AbsoluteExpiresAt);
            using var clientLease = BffSessionClientSerializer.Serialize(
                client,
                out var clientValue
            );
            var memberValueKey = BffStoreKeys.ClientMemberValue(member);
            // A value that is already present holds the same client, so it stays.
            await values.TryAddAsync(
                memberValueKey,
                clientValue,
                memberExpiresAt,
                cancellationToken
            );
            await indexes.AddMemberAsync(
                BffStoreKeys.ClientsIndex,
                member,
                memberExpiresAt,
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
            var clientIndex = BffStoreKeys.ClientIndex(client);
            await indexes.RemoveMemberAsync(clientIndex, storeKey, cancellationToken);

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
        IReadOnlyCollection<BffSessionClient> except,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(except);

        try
        {
            var members = await indexes.GetMembersAsync(
                BffStoreKeys.ClientsIndex,
                cancellationToken
            );

            var clients = new List<BffSessionClient>();
            var exceptDigests = except.Select(BffStoreKeys.ClientDigest);
            var resolved = new HashSet<string>(exceptDigests, StringComparer.Ordinal);
            foreach (var member in members)
            {
                if (
                    !BffStoreKeys.TryParseClientMember(member, out var digest)
                    || resolved.Contains(digest)
                )
                    continue;

                var memberValueKey = BffStoreKeys.ClientMemberValue(member);
                var stored = await values.GetAsync(memberValueKey, cancellationToken);
                if (
                    stored is not { } found
                    || BffSessionClientSerializer.Deserialize(found.Value.Span) is not { } client
                    || BffStoreKeys.ClientDigest(client) != digest
                )
                    continue;

                resolved.Add(digest);
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
    public async ValueTask ForgetEndedSessionAsync(
        BffSessionClient client,
        string storeKey,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var clientIndex = BffStoreKeys.ClientIndex(client);
            await indexes.RemoveMemberAsync(clientIndex, storeKey, cancellationToken);
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
}
