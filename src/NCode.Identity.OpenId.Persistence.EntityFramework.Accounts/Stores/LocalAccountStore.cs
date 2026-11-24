// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NCode.Identity.OpenId.Accounts.Credentials;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Persistence.EntityFramework.Accounts.Entities;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.OpenId.Persistence.EntityFramework.Stores;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Accounts.Stores;

/// <summary>
/// Provides the generic Entity Framework Core implementation of <see cref="ILocalAccountStore"/> and
/// <see cref="ILocalAccountTransferStore"/> over the shared <see cref="OpenIdDbContext"/>, reusing the framework store
/// base for id/concurrency generation and normalization. Credential handling is owned here: it hashes and verifies
/// passwords through the pluggable <see cref="IPasswordHasher"/> and keeps the hash a store-internal detail that never
/// leaves on the <see cref="PersistedLocalAccount"/> contract. An identifier that no account of the connection has, and
/// an account without a password, are verified against the hasher's dummy hash, so a wrong password takes as long
/// either way. Every account belongs to one connection, whose user names and email addresses are unique within it.
/// </summary>
internal sealed class LocalAccountStore(
    IStoreProvider storeProvider,
    ISurrogateKeyGenerator keyGenerator,
    IPageCursorProtector cursorProtector,
    IPasswordHasher passwordHasher,
    OpenIdDbContext openIdDbContext
)
    : BaseStore<PersistedLocalAccount, LocalAccountEntity>,
        ILocalAccountStore,
        ILocalAccountTransferStore
{
    /// <inheritdoc />
    protected override IStoreProvider StoreProvider { get; } = storeProvider;

    /// <inheritdoc />
    protected override ISurrogateKeyGenerator KeyGenerator { get; } = keyGenerator;

    /// <inheritdoc />
    protected override IPageCursorProtector CursorProtector { get; } = cursorProtector;

    /// <inheritdoc />
    protected override OpenIdDbContext DbContext { get; } = openIdDbContext;

    private IPasswordHasher PasswordHasher { get; } = passwordHasher;

    private DbSet<LocalAccountEntity> Accounts => DbContext.Set<LocalAccountEntity>();

    /// <inheritdoc />
    protected override ValueTask<PersistedLocalAccount> MapFromEntityAsync(
        LocalAccountEntity entity,
        CancellationToken cancellationToken
    )
    {
        var persisted = MapFromEntity(entity);
        return ValueTask.FromResult(persisted);
    }

    /// <inheritdoc />
    protected override async ValueTask<LocalAccountEntity?> GetEntityOrDefaultAsync(
        Expression<Func<LocalAccountEntity, bool>> predicate,
        CancellationToken cancellationToken
    ) =>
        await Accounts
            .Include(entity => entity.Tenant)
            .Include(entity => entity.Connection)
            .Include(entity => entity.Claims)
            .FirstOrDefaultAsync(predicate, cancellationToken);

    /// <inheritdoc />
    protected override async ValueTask<IReadOnlyList<LocalAccountEntity>> GetEntityPageAsync(
        Guid? afterId,
        int take,
        CancellationToken cancellationToken
    )
    {
        var query = Accounts
            .Include(entity => entity.Tenant)
            .Include(entity => entity.Connection)
            .Include(entity => entity.Claims)
            .AsQueryable();

        if (afterId is { } id)
        {
            query = query.Where(entity => entity.Id > id);
        }

        return await query.OrderBy(entity => entity.Id).Take(take).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override Guid GetSortKey(LocalAccountEntity entity) => entity.Id;

    /// <inheritdoc />
    public async ValueTask AddAsync(
        PersistedLocalAccount account,
        ReadOnlyMemory<byte>? password,
        CancellationToken cancellationToken
    )
    {
        account.ConcurrencyToken = NextConcurrencyToken();

        var tenantEntity = await GetTenantEntityAsync(account.TenantId, cancellationToken);
        var connectionEntity = await GetTenantConnectionAsync(
            tenantEntity,
            account.ConnectionId,
            cancellationToken
        );
        var normalizedLocalAccountId = Normalize(account.LocalAccountId);
        var normalizedUserName = Normalize(account.UserName);
        var normalizedEmail = Normalize(account.Email);

        var entity = new LocalAccountEntity
        {
            Id = NextId(),
            TenantId = tenantEntity.Id,
            NormalizedTenantId = tenantEntity.NormalizedTenantId,
            Tenant = tenantEntity,
            ConnectionId = connectionEntity.Id,
            Connection = connectionEntity,
            LocalAccountId = account.LocalAccountId,
            NormalizedLocalAccountId = normalizedLocalAccountId,
            UserName = account.UserName,
            DisplayName = account.DisplayName,
            NormalizedUserName = normalizedUserName,
            Email = account.Email,
            NormalizedEmail = normalizedEmail,
            EmailVerified = account.EmailVerified,
            PasswordHash = password is { } value ? PasswordHasher.HashPassword(value.Span) : null,
            SecurityStamp = NextSecurityStamp(),
            IsEnabled = account.IsEnabled,
            ConcurrencyToken = account.ConcurrencyToken,
        };

        foreach (var claim in account.Claims)
        {
            var claimEntity = new LocalAccountClaimEntity
            {
                Id = NextId(),
                TenantId = tenantEntity.Id,
                NormalizedTenantId = tenantEntity.NormalizedTenantId,
                Tenant = tenantEntity,
                Type = claim.Type,
                Value = claim.Value,
            };
            entity.Claims.Add(claimEntity);
        }

        await Accounts.AddAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask UpdateAsync(
        PersistedLocalAccount account,
        CancellationToken cancellationToken
    )
    {
        var entity = await GetEntityByIdAsync(account.LocalAccountId, cancellationToken);

        entity.UserName = account.UserName;
        entity.DisplayName = account.DisplayName;
        entity.NormalizedUserName = Normalize(account.UserName);
        entity.Email = account.Email;
        entity.NormalizedEmail = Normalize(account.Email);
        entity.EmailVerified = account.EmailVerified;
        entity.IsEnabled = account.IsEnabled;
    }

    /// <inheritdoc />
    public async ValueTask SetPasswordAsync(
        string localAccountId,
        ReadOnlyMemory<byte> password,
        CancellationToken cancellationToken
    )
    {
        var entity = await GetEntityByIdAsync(localAccountId, cancellationToken);

        entity.PasswordHash = PasswordHasher.HashPassword(password.Span);
        // Rotating the security stamp invalidates outstanding sessions and tokens issued before the reset.
        entity.SecurityStamp = NextSecurityStamp();
    }

    /// <inheritdoc />
    public async ValueTask<LocalAccountVerificationResult> VerifyCredentialAsync(
        LocalAccountSignInIdentifier identifier,
        ReadOnlyMemory<byte> password,
        Func<string, CancellationToken, ValueTask<bool>> isRefusedAsync,
        CancellationToken cancellationToken
    )
    {
        var connectionEntity = await GetConnectionOrDefaultAsync(
            identifier.ConnectionId,
            cancellationToken
        );
        var entity = connectionEntity is null
            ? null
            : await GetEntityByIdentifierOrDefaultAsync(
                connectionEntity,
                identifier,
                cancellationToken
            );
        // The limit counts the account the store found, or the identifier the end user submitted, and a refused
        // password is verified against the dummy hash, so a refusal costs what a wrong password costs. See ADR-1237.
        var accountKey = entity is null
            ? LocalAccountAttemptKeys.ForIdentifier(
                identifier.UserName ?? identifier.Email ?? string.Empty
            )
            : LocalAccountAttemptKeys.ForAccount(entity.LocalAccountId);
        var isRefused = await isRefusedAsync(accountKey, cancellationToken);
        // A disabled account's credential is verified too, so the sign-in can tell the end user why it is refused.
        // See ADR-1229.
        var passwordHash =
            !isRefused && entity?.PasswordHash is { Length: > 0 } storedHash ? storedHash : null;
        // An unknown identifier and an account without a password are verified against the dummy hash, so a wrong
        // password takes as long whether or not an account matches. See ADR-1239.
        var verifiedHash = passwordHash ?? PasswordHasher.DummyPasswordHash;
        var result = PasswordHasher.VerifyHashedPassword(verifiedHash, password.Span);
        if (entity is null || passwordHash is null || result == PasswordVerificationResult.Failed)
        {
            return new LocalAccountVerificationResult
            {
                AccountKey = accountKey,
                IsRefused = isRefused,
            };
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // Transparently upgrade the stored hash to the current parameters without changing the credential.
            entity.PasswordHash = PasswordHasher.HashPassword(password.Span);
        }

        return new LocalAccountVerificationResult
        {
            AccountKey = accountKey,
            Account = MapFromEntity(entity),
        };
    }

    /// <inheritdoc />
    public ValueTask<PersistedLocalAccount?> GetByIdOrDefaultAsync(
        string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var normalizedLocalAccountId = Normalize(localAccountId);
        return GetOrDefaultAsync(
            entity => entity.NormalizedLocalAccountId == normalizedLocalAccountId,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask<PersistedLocalAccount?> GetByUserNameOrDefaultAsync(
        string connectionId,
        string userName,
        CancellationToken cancellationToken
    )
    {
        var connectionEntity = await GetConnectionOrDefaultAsync(connectionId, cancellationToken);
        if (connectionEntity is null)
        {
            return null;
        }

        var entity = await GetEntityByUserNameOrDefaultAsync(
            connectionEntity,
            userName,
            cancellationToken
        );
        return entity is null ? null : MapFromEntity(entity);
    }

    /// <inheritdoc />
    public async ValueTask<PersistedLocalAccount?> GetByEmailOrDefaultAsync(
        string connectionId,
        string email,
        CancellationToken cancellationToken
    )
    {
        var connectionEntity = await GetConnectionOrDefaultAsync(connectionId, cancellationToken);
        if (connectionEntity is null)
        {
            return null;
        }

        var entity = await GetEntityByEmailOrDefaultAsync(
            connectionEntity,
            email,
            cancellationToken
        );
        return entity is null ? null : MapFromEntity(entity);
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<PersistedLocalAccount>> GetPageAsync(
        string connectionId,
        PageRequest page,
        CancellationToken cancellationToken
    )
    {
        var connectionEntity = await GetConnectionOrDefaultAsync(connectionId, cancellationToken);
        if (connectionEntity is null)
        {
            return new PagedResult<PersistedLocalAccount> { Items = [], NextCursor = null };
        }

        var afterId = DecodeCursor(page.Cursor);
        var connectionKey = connectionEntity.Id;
        var query = Accounts
            .Include(entity => entity.Tenant)
            .Include(entity => entity.Connection)
            .Include(entity => entity.Claims)
            .Where(entity => entity.ConnectionId == connectionKey);

        if (afterId is { } id)
        {
            query = query.Where(entity => entity.Id > id);
        }

        // Fetch one extra row so a full page signals that another page exists.
        var entities = await query
            .OrderBy(entity => entity.Id)
            .Take(page.Limit + 1)
            .ToListAsync(cancellationToken);

        return await BuildPageAsync(entities, page.Limit, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<LocalAccountNameProjection>> GetNamesByIdsAsync(
        IReadOnlyCollection<string> localAccountIds,
        CancellationToken cancellationToken
    )
    {
        if (localAccountIds.Count == 0)
        {
            return [];
        }

        var normalizedIds = new List<string>(localAccountIds.Count);
        foreach (var localAccountId in localAccountIds)
        {
            var normalizedId = Normalize(localAccountId);
            normalizedIds.Add(normalizedId);
        }

        return await Accounts
            .Where(entity => normalizedIds.Contains(entity.NormalizedLocalAccountId))
            .Select(entity => new LocalAccountNameProjection
            {
                LocalAccountId = entity.LocalAccountId,
                ConnectionId = entity.Connection.ConnectionId,
                UserName = entity.UserName,
                DisplayName = entity.DisplayName,
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<LocalAccountNameProjection>> GetNamesByUserNamesAsync(
        string connectionId,
        IReadOnlyCollection<string> userNames,
        CancellationToken cancellationToken
    )
    {
        if (userNames.Count == 0)
        {
            return [];
        }

        var connectionEntity = await GetConnectionOrDefaultAsync(connectionId, cancellationToken);
        if (connectionEntity is null)
        {
            return [];
        }

        var normalizedUserNames = new List<string>(userNames.Count);
        foreach (var userName in userNames)
        {
            var normalizedUserName = Normalize(userName);
            normalizedUserNames.Add(normalizedUserName);
        }

        var connectionKey = connectionEntity.Id;
        return await Accounts
            .Where(entity =>
                entity.ConnectionId == connectionKey
                && entity.NormalizedUserName != null
                && normalizedUserNames.Contains(entity.NormalizedUserName)
            )
            .Select(entity => new LocalAccountNameProjection
            {
                LocalAccountId = entity.LocalAccountId,
                ConnectionId = entity.Connection.ConnectionId,
                UserName = entity.UserName,
                DisplayName = entity.DisplayName,
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string localAccountId, CancellationToken cancellationToken)
    {
        var entity = await GetEntityByIdOrDefaultAsync(localAccountId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        DbContext.Set<LocalAccountClaimEntity>().RemoveRange(entity.Claims);
        Accounts.Remove(entity);
    }

    /// <inheritdoc />
    public async ValueTask ReplaceClaimsAsync(
        string localAccountId,
        IReadOnlyList<PersistedLocalAccountClaim> claims,
        CancellationToken cancellationToken
    )
    {
        var entity = await GetEntityByIdAsync(localAccountId, cancellationToken);

        DbContext.Set<LocalAccountClaimEntity>().RemoveRange(entity.Claims);
        entity.Claims.Clear();

        foreach (var claim in claims)
        {
            var claimEntity = new LocalAccountClaimEntity
            {
                Id = NextId(),
                TenantId = entity.TenantId,
                NormalizedTenantId = entity.NormalizedTenantId,
                Tenant = entity.Tenant,
                Type = claim.Type,
                Value = claim.Value,
            };
            entity.Claims.Add(claimEntity);
        }

        // Touch the account row so its concurrency token bumps (the claims collection change alone would not mark it).
        entity.ConcurrencyToken = NextConcurrencyToken();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<LocalAccountTransferRecord>> ExportAsync(
        string connectionId,
        CancellationToken cancellationToken
    )
    {
        var connectionEntity = await GetConnectionOrDefaultAsync(connectionId, cancellationToken);
        if (connectionEntity is null)
        {
            return [];
        }

        var connectionKey = connectionEntity.Id;
        var entities = await Accounts
            .Include(entity => entity.Claims)
            .Where(entity => entity.ConnectionId == connectionKey)
            .OrderBy(entity => entity.Id)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToTransferRecord).ToList();
    }

    /// <inheritdoc />
    public async ValueTask ImportAsync(
        string connectionId,
        IReadOnlyList<LocalAccountTransferRecord> records,
        CancellationToken cancellationToken
    )
    {
        if (records.Count == 0)
        {
            return;
        }

        var tenantId =
            DbContext.NormalizedAmbientTenantId
            ?? throw new InvalidOperationException(
                "Local accounts cannot be imported without a tenant scope. Open a tenant scope with IAmbientTenantAccessor.BeginScope."
            );
        var tenantEntity = await GetTenantEntityAsync(tenantId, cancellationToken);
        var connectionEntity = await GetTenantConnectionAsync(
            tenantEntity,
            connectionId,
            cancellationToken
        );

        foreach (var record in records)
        {
            // Another store's export carries its own normalized forms, so this store derives them, as AddAsync does.
            var normalizedLocalAccountId = Normalize(record.LocalAccountId);
            var normalizedUserName = Normalize(record.UserName);
            var normalizedEmail = Normalize(record.Email);
            var entity = new LocalAccountEntity
            {
                Id = NextId(),
                TenantId = tenantEntity.Id,
                NormalizedTenantId = tenantEntity.NormalizedTenantId,
                Tenant = tenantEntity,
                ConnectionId = connectionEntity.Id,
                Connection = connectionEntity,
                LocalAccountId = record.LocalAccountId,
                NormalizedLocalAccountId = normalizedLocalAccountId,
                UserName = record.UserName,
                DisplayName = record.DisplayName,
                NormalizedUserName = normalizedUserName,
                Email = record.Email,
                NormalizedEmail = normalizedEmail,
                EmailVerified = record.EmailVerified,
                PasswordHash = record.PasswordHash,
                SecurityStamp = record.SecurityStamp,
                IsEnabled = record.IsEnabled,
                ConcurrencyToken = NextConcurrencyToken(),
            };

            foreach (var claim in record.Claims)
            {
                var claimEntity = new LocalAccountClaimEntity
                {
                    Id = NextId(),
                    TenantId = tenantEntity.Id,
                    NormalizedTenantId = tenantEntity.NormalizedTenantId,
                    Tenant = tenantEntity,
                    Type = claim.Type,
                    Value = claim.Value,
                };
                entity.Claims.Add(claimEntity);
            }

            await Accounts.AddAsync(entity, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask RemoveAllAsync(string connectionId, CancellationToken cancellationToken)
    {
        var connectionEntity = await GetConnectionOrDefaultAsync(connectionId, cancellationToken);
        if (connectionEntity is null)
        {
            return;
        }

        var connectionKey = connectionEntity.Id;
        var entities = await Accounts
            .Include(entity => entity.Claims)
            .Where(entity => entity.ConnectionId == connectionKey)
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
        {
            DbContext.Set<LocalAccountClaimEntity>().RemoveRange(entity.Claims);
        }

        Accounts.RemoveRange(entities);
    }

    private static string NextSecurityStamp() => Guid.NewGuid().ToString("N");

    private async ValueTask<TenantEntity> GetTenantEntityAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var normalizedTenantId = Normalize(tenantId);
        var tenantEntity = await DbContext
            .Set<TenantEntity>()
            .SingleOrDefaultAsync(
                tenant => tenant.NormalizedTenantId == normalizedTenantId,
                cancellationToken
            );
        return tenantEntity
            ?? throw new InvalidOperationException(
                $"An OpenId Tenant with '{tenantId}' was not found."
            );
    }

    private async ValueTask<ConnectionEntity?> GetConnectionOrDefaultAsync(
        string connectionId,
        CancellationToken cancellationToken
    )
    {
        var normalizedConnectionId = Normalize(connectionId);
        var isUnscoped = DbContext.IsUnscoped;
        var ambientTenantId = DbContext.NormalizedAmbientTenantId;
        // The change tracker answers first, so a connection added in the same unit of work resolves.
        var tracked = DbContext
            .Set<ConnectionEntity>()
            .Local.FirstOrDefault(connection =>
                connection.NormalizedConnectionId == normalizedConnectionId
                && (isUnscoped || connection.NormalizedTenantId == ambientTenantId)
            );
        return tracked
            ?? await DbContext
                .Set<ConnectionEntity>()
                .SingleOrDefaultAsync(
                    connection => connection.NormalizedConnectionId == normalizedConnectionId,
                    cancellationToken
                );
    }

    private async ValueTask<ConnectionEntity> GetTenantConnectionAsync(
        TenantEntity tenantEntity,
        string connectionId,
        CancellationToken cancellationToken
    )
    {
        var normalizedConnectionId = Normalize(connectionId);
        var tenantKey = tenantEntity.Id;
        // The change tracker answers first, so a connection added in the same unit of work resolves.
        var tracked = DbContext
            .Set<ConnectionEntity>()
            .Local.FirstOrDefault(connection =>
                connection.TenantId == tenantKey
                && connection.NormalizedConnectionId == normalizedConnectionId
            );
        var connectionEntity =
            tracked
            ?? await DbContext
                .Set<ConnectionEntity>()
                // Cross-tenant: the account names its tenant, which need not be the ambient tenant when written.
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    connection =>
                        connection.TenantId == tenantKey
                        && connection.NormalizedConnectionId == normalizedConnectionId,
                    cancellationToken
                );
        return connectionEntity
            ?? throw new InvalidOperationException(
                $"A connection with '{connectionId}' was not found."
            );
    }

    private ValueTask<LocalAccountEntity?> GetEntityByUserNameOrDefaultAsync(
        ConnectionEntity connectionEntity,
        string userName,
        CancellationToken cancellationToken
    )
    {
        var connectionKey = connectionEntity.Id;
        var normalizedUserName = Normalize(userName);
        return GetEntityOrDefaultAsync(
            entity =>
                entity.ConnectionId == connectionKey
                && entity.NormalizedUserName == normalizedUserName,
            cancellationToken
        );
    }

    private ValueTask<LocalAccountEntity?> GetEntityByEmailOrDefaultAsync(
        ConnectionEntity connectionEntity,
        string email,
        CancellationToken cancellationToken
    )
    {
        var connectionKey = connectionEntity.Id;
        var normalizedEmail = Normalize(email);
        return GetEntityOrDefaultAsync(
            entity =>
                entity.ConnectionId == connectionKey && entity.NormalizedEmail == normalizedEmail,
            cancellationToken
        );
    }

    // Each value the identifier carries is looked up whatever the other finds, so the work does not tell which one
    // matched; the user name's match wins.
    private async ValueTask<LocalAccountEntity?> GetEntityByIdentifierOrDefaultAsync(
        ConnectionEntity connectionEntity,
        LocalAccountSignInIdentifier identifier,
        CancellationToken cancellationToken
    )
    {
        LocalAccountEntity? byUserName = null;
        if (identifier.UserName is { } userName)
        {
            byUserName = await GetEntityByUserNameOrDefaultAsync(
                connectionEntity,
                userName,
                cancellationToken
            );
        }

        LocalAccountEntity? byEmail = null;
        if (identifier.Email is { } email)
        {
            byEmail = await GetEntityByEmailOrDefaultAsync(
                connectionEntity,
                email,
                cancellationToken
            );
        }

        return byUserName ?? byEmail;
    }

    private ValueTask<LocalAccountEntity?> GetEntityByIdOrDefaultAsync(
        string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var normalizedLocalAccountId = Normalize(localAccountId);
        return GetEntityOrDefaultAsync(
            entity => entity.NormalizedLocalAccountId == normalizedLocalAccountId,
            cancellationToken
        );
    }

    private async ValueTask<LocalAccountEntity> GetEntityByIdAsync(
        string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var entity = await GetEntityByIdOrDefaultAsync(localAccountId, cancellationToken);
        return entity
            ?? throw new InvalidOperationException(
                $"A local account with '{localAccountId}' was not found."
            );
    }

    // The store keeps no lockout counter, so a disabled account is the account locked out for good.
    private static LocalAccountTransferRecord MapToTransferRecord(LocalAccountEntity entity) =>
        new()
        {
            LocalAccountId = entity.LocalAccountId,
            UserName = entity.UserName,
            DisplayName = entity.DisplayName,
            NormalizedUserName = entity.NormalizedUserName,
            Email = entity.Email,
            NormalizedEmail = entity.NormalizedEmail,
            EmailVerified = entity.EmailVerified,
            PasswordHash = entity.PasswordHash,
            SecurityStamp = entity.SecurityStamp,
            IsEnabled = entity.IsEnabled,
            LockoutEnabled = !entity.IsEnabled,
            LockoutEnd = entity.IsEnabled ? null : DateTimeOffset.MaxValue,
            AccessFailedCount = 0,
            Claims = entity
                .Claims.Select(claim => new PersistedLocalAccountClaim
                {
                    Type = claim.Type,
                    Value = claim.Value,
                })
                .ToList(),
        };

    private static PersistedLocalAccount MapFromEntity(LocalAccountEntity entity) =>
        new()
        {
            TenantId = entity.Tenant.TenantId,
            ConnectionId = entity.Connection.ConnectionId,
            LocalAccountId = entity.LocalAccountId,
            UserName = entity.UserName,
            DisplayName = entity.DisplayName,
            Email = entity.Email,
            EmailVerified = entity.EmailVerified,
            SecurityStamp = entity.SecurityStamp,
            IsEnabled = entity.IsEnabled,
            Claims = entity
                .Claims.Select(claim => new PersistedLocalAccountClaim
                {
                    Type = claim.Type,
                    Value = claim.Value,
                })
                .ToList(),
            ConcurrencyToken = entity.ConcurrencyToken,
        };
}
