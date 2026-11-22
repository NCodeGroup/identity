// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.Persistence.Logging;

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Provides the default implementation of <see cref="IMasterKeyRewrapper"/>.
/// </summary>
/// <remarks>
/// Each row is rewrapped in its own unit of work. A row that vanished during the run, whether its update conflicts or
/// finds it gone, counts as examined only. The material of a master key does not change, so no replica needs to reload
/// it. The target of a row is the root of its owner: the nearest root key that is not being removed, the owner's own or
/// else its nearest ancestor's, otherwise the configured root key provider. A row that already sits under that root key
/// is examined only, and a row that is moved under it loses its recovery copy; a row moved under the deployment's root
/// key gets a recovery copy when a recovery public key is configured, and when none is, the existing recovery copy is
/// left as it is, because a recovery path is never removed silently. A request for an owner and its descendants reads
/// the rows under the key its root key names, then the owners that hold master keys and the rows of each owner under
/// it, so no wrapped key of another owner is read, and it resolves the root key of the owners it keeps only.
/// </remarks>
// See ADR-1408 and ADR-1414.
internal sealed class DefaultMasterKeyRewrapper(
    MasterKeyFactory factory,
    INearestOwnerRootKeyResolver nearestRootKeyResolver,
    ISecretOwnerParentResolver parentResolver,
    IStoreManagerFactory storeManagerFactory,
    IRecoveryKeyWrapper recovery,
    ILogger<DefaultMasterKeyRewrapper> logger
) : IMasterKeyRewrapper
{
    private const int PageSize = 100;

    private const int MaxAttempts = 3;

    private MasterKeyFactory Factory { get; } = factory;

    private INearestOwnerRootKeyResolver NearestRootKeyResolver { get; } = nearestRootKeyResolver;

    private ISecretOwnerParentResolver ParentResolver { get; } = parentResolver;

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private IRecoveryKeyWrapper Recovery { get; } = recovery;

    private ILogger<DefaultMasterKeyRewrapper> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask<MasterKeyRewrapResult> RewrapAsync(
        MasterKeyRewrapRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RecoveryOnly && !Recovery.IsConfigured)
        {
            throw new InvalidOperationException(
                "A recovery-only rewrap needs a recovery public key; configure SecretProtection:Recovery:PublicKeyPath."
            );
        }

        using var unwrapper = request.RecoveryPrivateKeyPem.IsEmpty
            ? null
            : RecoveryKeyUnwrapper.FromPem(request.RecoveryPrivateKeyPem.Span);

        var total = new MasterKeyRewrapResult(0, 0, 0, 0);
        var rootKeys = new Dictionary<SecretOwner, PersistedOwnerRootKey?>(
            SecretOwnerComparer.Instance
        );

        if (request.Owner is { } owner && !request.IncludeDescendants)
        {
            var rows = await ListAsync(owner, rootKeys, cancellationToken);
            foreach (var row in rows)
            {
                var processed = await ProcessAsync(
                    row,
                    request,
                    unwrapper,
                    rootKeys,
                    cancellationToken
                );
                total = Add(total, processed);
            }
        }
        else
        {
            // The platform owner is every other owner's ancestor, so a request for it keeps every row.
            var scope =
                request.Owner is { } root
                && !string.Equals(
                    root.OwnerType,
                    SecretOwnerTypes.Platform,
                    StringComparison.OrdinalIgnoreCase
                )
                    ? root.Normalize()
                    : (SecretOwner?)null;

            if (scope is { } scoped)
            {
                total = await RewrapScopeAsync(
                    request,
                    scoped,
                    unwrapper,
                    rootKeys,
                    cancellationToken
                );
            }
            else
            {
                string? cursor = null;
                do
                {
                    var page = await GetPageAsync(cursor, rootKeys, cancellationToken);
                    foreach (var row in page.Items)
                    {
                        var processed = await ProcessAsync(
                            row,
                            request,
                            unwrapper,
                            rootKeys,
                            cancellationToken
                        );
                        total = Add(total, processed);
                    }

                    cursor = page.NextCursor;
                } while (cursor is not null);
            }
        }

        Logger.MasterKeyRewrapCompleted(
            total.Examined,
            total.Rewrapped,
            total.Verified,
            total.Failed
        );

        return total;
    }

    private static MasterKeyRewrapResult Add(
        MasterKeyRewrapResult total,
        MasterKeyRewrapResult row
    ) =>
        new(
            total.Examined + row.Examined,
            total.Rewrapped + row.Rewrapped,
            total.Verified + row.Verified,
            total.Failed + row.Failed
        );

    // Lists the rows of an owner and reads the owner's root key through the same unit of work.
    private async ValueTask<IReadOnlyList<PersistedMasterKey>> ListAsync(
        SecretOwner owner,
        Dictionary<SecretOwner, PersistedOwnerRootKey?> rootKeys,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IMasterKeyStore>();
        var normalizedOwner = owner.Normalize();
        var rows = await store.ListAsync(normalizedOwner, cancellationToken);

        await ReadRootKeysAsync(storeManager, rows, rootKeys, cancellationToken);
        return rows;
    }

    // Reads a page of every row and the root key of each owner on it that this run has not read yet through the same
    // unit of work.
    private async ValueTask<PagedResult<PersistedMasterKey>> GetPageAsync(
        string? cursor,
        Dictionary<SecretOwner, PersistedOwnerRootKey?> rootKeys,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IMasterKeyStore>();
        var pageRequest = new PageRequest { Limit = PageSize, Cursor = cursor };
        var page = await store.GetPageAsync(pageRequest, cancellationToken);

        await ReadRootKeysAsync(storeManager, page.Items, rootKeys, cancellationToken);
        return page;
    }

    // Rewraps the rows of an owner and of its descendants, and the rows that still sit under the key the root key of the
    // owner names, whatever its state. It reads the rows under that key, then the owners that hold master keys and,
    // for each owner under the scope, the rows of that owner, never every master key's wrapped material. A row that
    // both reads return is processed once.
    private async ValueTask<MasterKeyRewrapResult> RewrapScopeAsync(
        MasterKeyRewrapRequest request,
        SecretOwner scope,
        RecoveryKeyUnwrapper? unwrapper,
        Dictionary<SecretOwner, PersistedOwnerRootKey?> rootKeys,
        CancellationToken cancellationToken
    )
    {
        var total = new MasterKeyRewrapResult(0, 0, 0, 0);
        var processedKeyIds = new HashSet<Guid>();

        var key = await FindRootKeyAsync(scope, cancellationToken);
        if (key is not null)
        {
            string? keyCursor = null;
            do
            {
                var keyPage = await GetWrappedByKeyPageAsync(
                    key,
                    keyCursor,
                    rootKeys,
                    cancellationToken
                );
                foreach (var row in keyPage.Items)
                {
                    processedKeyIds.Add(row.KeyId);
                    var processed = await ProcessAsync(
                        row,
                        request,
                        unwrapper,
                        rootKeys,
                        cancellationToken
                    );
                    total = Add(total, processed);
                }

                keyCursor = keyPage.NextCursor;
            } while (keyCursor is not null);
        }

        var inherits = new Dictionary<SecretOwner, bool>(SecretOwnerComparer.Instance);
        var seenOwners = new HashSet<SecretOwner>(SecretOwnerComparer.Instance);
        string? ownerCursor = null;
        do
        {
            var ownerPage = await GetOwnerPageAsync(ownerCursor, cancellationToken);
            foreach (var owner in ownerPage.Items)
            {
                if (!seenOwners.Add(owner))
                    continue;

                var underScope = await InheritsAsync(owner, scope, inherits, cancellationToken);
                if (!underScope)
                    continue;

                var rows = await ListAsync(owner, rootKeys, cancellationToken);
                foreach (var row in rows)
                {
                    if (!processedKeyIds.Add(row.KeyId))
                        continue;

                    var processed = await ProcessAsync(
                        row,
                        request,
                        unwrapper,
                        rootKeys,
                        cancellationToken
                    );
                    total = Add(total, processed);
                }
            }

            ownerCursor = ownerPage.NextCursor;
        } while (ownerCursor is not null);

        return total;
    }

    // Reads the root key of an owner, whatever its state, which is the key that its rows are wrapped under.
    private async ValueTask<PersistedOwnerRootKey?> FindRootKeyAsync(
        SecretOwner owner,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IOwnerRootKeyStore>();
        return await store.FindAsync(owner, cancellationToken);
    }

    // Reads a page of the rows wrapped under a root key and the root key of each owner on it that this run has not read
    // yet through the same unit of work.
    private async ValueTask<PagedResult<PersistedMasterKey>> GetWrappedByKeyPageAsync(
        PersistedOwnerRootKey key,
        string? cursor,
        Dictionary<SecretOwner, PersistedOwnerRootKey?> rootKeys,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IMasterKeyStore>();
        var pageRequest = new PageRequest { Limit = PageSize, Cursor = cursor };
        var page = await store.GetWrappedByKeyPageAsync(
            key.KeyWrapperName,
            key.KeyReference,
            pageRequest,
            cancellationToken
        );

        await ReadRootKeysAsync(storeManager, page.Items, rootKeys, cancellationToken);
        return page;
    }

    // Reads a page of the owners that hold master keys, which carries no wrapped material.
    private async ValueTask<PagedResult<SecretOwner>> GetOwnerPageAsync(
        string? cursor,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IMasterKeyStore>();
        var pageRequest = new PageRequest { Limit = PageSize, Cursor = cursor };
        return await store.GetOwnerPageAsync(pageRequest, cancellationToken);
    }

    // Determines whether a candidate is the owner or has the owner among its ancestors, once per candidate in a run.
    private async ValueTask<bool> InheritsAsync(
        SecretOwner candidate,
        SecretOwner owner,
        Dictionary<SecretOwner, bool> inherits,
        CancellationToken cancellationToken
    )
    {
        if (inherits.TryGetValue(candidate, out var known))
            return known;

        var result = SecretOwnerComparer.Instance.Equals(candidate, owner);
        if (!result)
        {
            var normalizedCandidate = candidate.Normalize();
            var ancestors = await ParentResolver.GetAncestorsAsync(
                normalizedCandidate,
                cancellationToken
            );
            result = ancestors.Contains(owner, SecretOwnerComparer.Instance);
        }

        inherits[candidate] = result;
        return result;
    }

    private async ValueTask ReadRootKeysAsync(
        IStoreManager storeManager,
        IEnumerable<PersistedMasterKey> rows,
        Dictionary<SecretOwner, PersistedOwnerRootKey?> rootKeys,
        CancellationToken cancellationToken
    )
    {
        foreach (var owner in rows.Select(row => row.Owner).Distinct(SecretOwnerComparer.Instance))
        {
            if (rootKeys.ContainsKey(owner))
                continue;

            var ownerRootKeyStore = storeManager.GetStore<IOwnerRootKeyStore>();
            rootKeys[owner] = await NearestRootKeyResolver.FindNearestAsync(
                ownerRootKeyStore,
                owner,
                cancellationToken
            );
        }
    }

    // Rewraps one row in its own unit of work, retrying when another writer changed the row, and returns the outcome.
    private async ValueTask<MasterKeyRewrapResult> ProcessAsync(
        PersistedMasterKey row,
        MasterKeyRewrapRequest request,
        RecoveryKeyUnwrapper? unwrapper,
        Dictionary<SecretOwner, PersistedOwnerRootKey?> rootKeys,
        CancellationToken cancellationToken
    )
    {
        try
        {
            rootKeys.TryGetValue(row.Owner, out var rootKey);
            var ownerRooted = MasterKeyFactory.IsOwnerRooted(rootKey);

            for (var attempt = 1; ; attempt++)
            {
                var conforms = MasterKeyFactory.Conforms(row, rootKey);

                // A row that conforms is left as it is: the online conformance reads nothing of it, and a dry run
                // still proves that its owner root key unwraps it.
                if (request.NonconformingOnly && conforms)
                    return new MasterKeyRewrapResult(1, 0, 0, 0);

                if (ownerRooted && conforms)
                {
                    if (!request.DryRun)
                        return new MasterKeyRewrapResult(1, 0, 0, 0);

                    using var verified = await Factory.UnwrapAsync(row, cancellationToken);
                    return new MasterKeyRewrapResult(1, 0, 1, 0);
                }

                // A recovery-only run on an owner root key only removes a recovery copy that a row under that key still
                // carries; a row still under the deployment's root key keeps, or gains, its recovery copy.
                if (
                    request is { RecoveryOnly: true }
                    && ownerRooted
                    && row.RootKeyReference is not null
                    && row.RecoveryWrappedKey is null
                    && row.RecoveryKeyId is null
                )
                {
                    return new MasterKeyRewrapResult(1, 0, 0, 0);
                }

                // A row under an owner root key has no recovery copy, so it unwraps through its own key.
                using var masterKey =
                    unwrapper is null || row.RootKeyReference is not null
                        ? await Factory.UnwrapAsync(row, cancellationToken)
                        : MasterKeyFactory.UnwrapWithRecovery(row, unwrapper);

                if (request.DryRun)
                {
                    return new MasterKeyRewrapResult(1, 0, 1, 0);
                }

                await ApplyWrapsAsync(row, masterKey, rootKey, request, cancellationToken);

                try
                {
                    await SaveAsync(row, cancellationToken);
                    return new MasterKeyRewrapResult(1, 1, 0, 0);
                }
                catch (Exception exception)
                    when (exception is StoreConcurrencyException or InvalidOperationException)
                {
                    // A row the maintenance pass destroyed during the run is gone, and counts as examined only.
                    var (current, currentRootKey) = await FindAsync(row, cancellationToken);
                    if (current is null)
                        return new MasterKeyRewrapResult(1, 0, 0, 0);

                    // Another writer changed the row, so it is read again and rewrapped from its current state. The
                    // conflicting writer is often a change of the owner's root key, so the row is judged against the
                    // binding read in the same unit of work, which replaces the one this run cached for the owner.
                    if (exception is not StoreConcurrencyException || attempt >= MaxAttempts)
                        throw;

                    row = current;
                    rootKey = currentRootKey;
                    rootKeys[row.Owner] = rootKey;
                    ownerRooted = MasterKeyFactory.IsOwnerRooted(rootKey);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.MasterKeyRewrapRowFailed(exception, row.Owner, row.KeyId, row.Version);
            return new MasterKeyRewrapResult(1, 0, 0, 1);
        }
    }

    // Replaces the root wrap, unless only the recovery copy is replaced, and the recovery copy, on the row. Under an
    // owner root key the row loses its recovery copy and gains no other; under the deployment's root key the recovery
    // copy is replaced when a recovery public key is configured.
    private async ValueTask ApplyWrapsAsync(
        PersistedMasterKey row,
        MasterKey masterKey,
        PersistedOwnerRootKey? rootKey,
        MasterKeyRewrapRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.RecoveryOnly)
        {
            // A row still under the deployment's root key keeps its recovery copy, even for an owner that has a root
            // key, until the row is rewrapped under it.
            if (MasterKeyFactory.IsOwnerRooted(rootKey) && row.RootKeyReference is not null)
            {
                row.RecoveryKeyId = null;
                row.RecoveryWrappedKey = null;
            }
            else if (await Factory.WrapUnderRecoveryAsync(masterKey, cancellationToken) is { } only)
            {
                row.RecoveryKeyId = only.KeyId;
                row.RecoveryWrappedKey = only.Ciphertext;
            }

            return;
        }

        var wraps = await Factory.WrapAsync(masterKey, rootKey, cancellationToken);

        row.RootKeyProvider = wraps.Root.WrapperName;
        row.RootKeyId = wraps.Root.KeyId;
        row.RootKeyReference = wraps.RootKeyReference;
        row.RootKeyAccessIdentity = wraps.RootKeyAccessIdentity;
        row.RootKeyAccessBinding = wraps.RootKeyAccessBinding;
        row.RootWrappedKey = wraps.Root.Ciphertext;

        if (wraps.ClearsRecovery)
        {
            row.RecoveryKeyId = null;
            row.RecoveryWrappedKey = null;
        }
        else if (wraps.Recovery is { } recovery)
        {
            row.RecoveryKeyId = recovery.KeyId;
            row.RecoveryWrappedKey = recovery.Ciphertext;
        }
    }

    private async ValueTask SaveAsync(PersistedMasterKey row, CancellationToken cancellationToken)
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        await storeManager.GetStore<IMasterKeyStore>().UpdateAsync(row, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);
    }

    // Reads the row again and the root key that wraps it, the owner's own or the nearest ancestor's, through the same
    // unit of work.
    private async ValueTask<(PersistedMasterKey? Row, PersistedOwnerRootKey? RootKey)> FindAsync(
        PersistedMasterKey row,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IMasterKeyStore>();
        var normalizedOwner = row.Owner.Normalize();
        var rows = await store.FindByKeyIdsAsync(normalizedOwner, [row.KeyId], cancellationToken);

        var ownerRootKeyStore = storeManager.GetStore<IOwnerRootKeyStore>();
        var rootKey = await NearestRootKeyResolver.FindNearestAsync(
            ownerRootKeyStore,
            row.Owner,
            cancellationToken
        );

        return (rows.SingleOrDefault(), rootKey);
    }
}
