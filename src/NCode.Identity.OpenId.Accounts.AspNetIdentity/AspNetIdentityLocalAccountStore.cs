// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NCode.Buffers;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

/// <summary>
/// Adapts an ASP.NET Core Identity <see cref="UserManager{TUser}"/> to the backing-store-agnostic
/// <see cref="ILocalAccountStore"/> seam, so a host already running ASP.NET Core Identity exposes its users as local
/// accounts (enabling the resource-owner password grant and the local-account management endpoints) with no change to
/// the OpenID runtime. It hashes and verifies a password only through the user manager's
/// <see cref="NormalizingPasswordHasher{TUser}"/>, so it and the host's own ASP.NET Core Identity pages hash and verify
/// a password's NFC form, and stores the hash through the user store, so no password validator of ASP.NET Core Identity
/// judges a password the password policy accepted; security stamps and concurrency are owned by the
/// <see cref="UserManager{TUser}"/>. It verifies a password against a dummy hash when it holds no hash it may verify,
/// so a failed verification takes as long whether or not the user exists.
/// </summary>
/// <remarks>
/// Each user belongs to one tenant (<see cref="ITenantBoundUser.TenantId"/>). Creating a user stamps the tenant of the
/// ambient tenant scope and fails when no tenant scope is set. Every lookup treats a user of another tenant as not
/// found, inside a tenant scope. Lookups by user name, and credential verification, need a tenant scope: they find
/// nothing in an unset scope and throw in an unscoped scope (<see cref="IAmbientTenantAccessor.IsUnscoped"/>). Lookups
/// by identifier match the identifier in the query over the tenant's users, so they read across tenants only in an
/// unscoped scope; like listing, they need an ASP.NET Core Identity store that supports querying users.
/// <see cref="PersistedLocalAccount.TenantId"/> is mapped as empty; the resource-owner password grant authenticates
/// against the request's tenant. Each operation resolves the scoped <see cref="UserManager{TUser}"/> from a fresh
/// dependency-injection scope, so this adapter holds no scoped state and its writes commit immediately (they do not
/// enlist in the OpenID unit of work). The user manager stores every user with a user name, a normalized user name and
/// a security stamp, so a stored user without its user name or security stamp is malformed and reading it as a local
/// account throws; an export also throws for a user without its normalized user name.
/// </remarks>
/// <typeparam name="TUser">The ASP.NET Core Identity user type.</typeparam>
internal sealed class AspNetIdentityLocalAccountStore<TUser>(
    IServiceScopeFactory serviceScopeFactory,
    IStoreProvider storeProvider,
    AspNetIdentityDummyPasswordHash<TUser> dummyPasswordHash,
    IAmbientTenantAccessor ambientTenantAccessor
) : ILocalAccountStore, ILocalAccountTransferStore
    where TUser : IdentityUser, ITenantBoundUser, new()
{
    private const string LockoutUnsupportedMessage =
        "The configured ASP.NET Core Identity store does not support lockout, which is required to disable a local account.";

    private static async ValueTask<PersistedLocalAccount> MapAsync(
        UserManager<TUser> userManager,
        TUser user,
        CancellationToken cancellationToken
    )
    {
        var userName = GetRequiredMember(user.UserName, nameof(IdentityUser.UserName), user.Id);
        var securityStamp = GetRequiredMember(
            user.SecurityStamp,
            nameof(IdentityUser.SecurityStamp),
            user.Id
        );
        var claims = await userManager.GetClaimsAsync(user);
        var isEnabled =
            !userManager.SupportsUserLockout || !await userManager.IsLockedOutAsync(user);

        return new PersistedLocalAccount
        {
            // The store has restricted the user to the ambient tenant; the grant authenticates against the request's
            // tenant.
            TenantId = string.Empty,
            LocalAccountId = user.Id,
            UserName = userName,
            Email = user.Email,
            EmailVerified = user.EmailConfirmed,
            SecurityStamp = securityStamp,
            IsEnabled = isEnabled,
            Claims = claims
                .Select(claim => new PersistedLocalAccountClaim
                {
                    Type = claim.Type,
                    Value = claim.Value,
                })
                .ToList(),
            ConcurrencyToken = user.ConcurrencyStamp ?? string.Empty,
        };
    }

    private static async ValueTask EnableAsync(
        UserManager<TUser> userManager,
        TUser user,
        CancellationToken cancellationToken
    )
    {
        if (userManager.SupportsUserLockout)
        {
            var lockoutEndCleared = await userManager.SetLockoutEndDateAsync(
                user,
                lockoutEnd: null
            );
            ThrowIfFailed(lockoutEndCleared, "enable the local account");
        }
    }

    private static async ValueTask DisableAsync(
        UserManager<TUser> userManager,
        TUser user,
        CancellationToken cancellationToken
    )
    {
        ThrowIfLockoutUnsupported(userManager);

        var lockoutEnabled = await userManager.SetLockoutEnabledAsync(user, enabled: true);
        ThrowIfFailed(lockoutEnabled, "enable lockout for the local account");

        var lockoutEndSet = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        ThrowIfFailed(lockoutEndSet, "disable the local account");
    }

    /// <summary>
    /// Reads the first user of a query, asynchronously when the provider offers <see cref="IAsyncEnumerable{T}"/> (this
    /// package does not reference EF Core) and synchronously for an in-memory store.
    /// </summary>
    /// <param name="query">The query to read.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The first user that the query yields, or <c>null</c> when it yields none.</returns>
    private static async ValueTask<TUser?> FirstOrDefaultAsync(
        IQueryable<TUser> query,
        CancellationToken cancellationToken
    )
    {
        var first = query.Take(1);
        if (first is not IAsyncEnumerable<TUser> asyncUsers)
        {
            return first.FirstOrDefault();
        }

        await foreach (var user in asyncUsers.WithCancellation(cancellationToken))
        {
            return user;
        }

        return null;
    }

    /// <summary>
    /// Reads a query into a list, asynchronously when the provider offers <see cref="IAsyncEnumerable{T}"/> and
    /// synchronously otherwise.
    /// </summary>
    /// <param name="query">The query to read.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The users that the query yields, which is an empty list when it yields none.</returns>
    private static async ValueTask<List<TUser>> ToListAsync(
        IQueryable<TUser> query,
        CancellationToken cancellationToken
    )
    {
        if (query is not IAsyncEnumerable<TUser> asyncUsers)
        {
            return query.ToList();
        }

        var users = new List<TUser>();
        await foreach (var user in asyncUsers.WithCancellation(cancellationToken))
        {
            users.Add(user);
        }

        return users;
    }

    private static IUserPasswordStore<TUser> GetPasswordStore(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<IUserStore<TUser>>() as IUserPasswordStore<TUser>
        ?? throw new NotSupportedException(
            "The configured ASP.NET Core Identity store does not store passwords, which a local account's password requires."
        );

    private static IUserClaimStore<TUser> GetClaimStore(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<IUserStore<TUser>>() as IUserClaimStore<TUser>
        ?? throw new NotSupportedException(
            "The configured ASP.NET Core Identity store does not store claims, which a local account's claims require."
        );

    private static NormalizingPasswordHasher<TUser> GetPasswordHasher(
        UserManager<TUser> userManager
    ) =>
        userManager.PasswordHasher as NormalizingPasswordHasher<TUser>
        ?? throw new InvalidOperationException(
            $"The password hasher of UserManager<{typeof(TUser).Name}> is not a NormalizingPasswordHasher, so it would not hash and verify the NFC form of a password. Register the host's IPasswordHasher<TUser> before AddAspNetIdentityLocalAccountStore, or wrap it in NormalizingPasswordHasher<TUser>."
        );

    private static async ValueTask SetPasswordHashAsync(
        NormalizingPasswordHasher<TUser> passwordHasher,
        IUserPasswordStore<TUser> passwordStore,
        TUser user,
        string password,
        CancellationToken cancellationToken
    )
    {
        // The hash is made with the user manager's normalizing hasher and stored through the user store, so no password
        // validator of ASP.NET Core Identity judges a password the password policy accepted. See ADR-1238.
        var passwordHash = passwordHasher.HashPassword(user, password);
        await passwordStore.SetPasswordHashAsync(user, passwordHash, cancellationToken);
    }

    private static async ValueTask<string?> GetVerifiablePasswordHashAsync(
        UserManager<TUser> userManager,
        IUserPasswordStore<TUser> passwordStore,
        TUser user,
        CancellationToken cancellationToken
    )
    {
        // A locked-out user's password is not verified: the lockout defends against guessing, so a correct password
        // must not be told apart from a wrong one. See ADR-1229.
        if (userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        // An empty hash is no password, as in the Entity Framework store; the host's hasher would refuse it without the
        // work of a verification. See ADR-1239.
        var passwordHash = await passwordStore.GetPasswordHashAsync(user, cancellationToken);
        return string.IsNullOrEmpty(passwordHash) ? null : passwordHash;
    }

    // ASP.NET Core Identity's credential API is string-based, so the credential is decoded to a string only here.
    private static string DecodePassword(ReadOnlyMemory<byte> password) =>
        SecureEncoding.UTF8.GetString(password.Span);

    private static void ThrowIfFailed(IdentityResult result, string action)
    {
        if (result.Succeeded)
        {
            return;
        }

        var descriptions = result.Errors.Select(error => error.Description);
        var errors = string.Join("; ", descriptions);
        throw new InvalidOperationException($"Failed to {action}: {errors}");
    }

    /// <summary>
    /// Requires that the ASP.NET Core Identity store supports lockout, which is how a disabled local account is stored.
    /// </summary>
    /// <param name="userManager">The <see cref="UserManager{TUser}"/> whose store is checked.</param>
    /// <exception cref="NotSupportedException">The store has no lockout support.</exception>
    private static void ThrowIfLockoutUnsupported(UserManager<TUser> userManager)
    {
        if (!userManager.SupportsUserLockout)
        {
            throw new NotSupportedException(LockoutUnsupportedMessage);
        }
    }

    /// <summary>
    /// Gets a member that ASP.NET Core Identity's user manager stores with every user, and requires that the stored
    /// user has it.
    /// </summary>
    /// <param name="value">The member's value as the user is stored.</param>
    /// <param name="memberName">The name of the member, which the exception names.</param>
    /// <param name="localAccountId">The identifier of the stored user, which the exception names.</param>
    /// <returns>The member's value.</returns>
    /// <exception cref="InvalidOperationException">The member's value is <c>null</c> or empty.</exception>
    // See ADR-1395.
    private static string GetRequiredMember(string? value, string memberName, string localAccountId)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"The stored local account '{localAccountId}' has no {memberName}, which ASP.NET Core Identity's user manager stores with every user."
            );
        }

        return value;
    }

    private IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;
    private IStoreProvider StoreProvider { get; } = storeProvider;
    private AspNetIdentityDummyPasswordHash<TUser> DummyPasswordHash { get; } = dummyPasswordHash;

    /// <summary>
    /// Gets the accessor of the ambient tenant that scopes every lookup and stamps every new user.
    /// </summary>
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public object? GetService(Type serviceType) => StoreProvider.GetService(serviceType);

    /// <inheritdoc />
    public TStore GetStore<TStore>()
        where TStore : IStore => StoreProvider.GetStore<TStore>();

    /// <inheritdoc />
    public async ValueTask AddAsync(
        PersistedLocalAccount account,
        ReadOnlyMemory<byte>? password,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();

        // A disabled account is stored as a lockout, so a store without lockout support refuses it before any user is
        // created.
        if (!account.IsEnabled)
        {
            ThrowIfLockoutUnsupported(userManager);
        }

        // The id is assigned by the orchestrating create handler (it is also the self-issued identity's subject), so it
        // must be carried onto the Identity user rather than letting the store generate its own.
        var user = new TUser
        {
            Id = account.LocalAccountId,
            TenantId = GetRequiredTenantId(),
            UserName = account.UserName,
            Email = account.Email,
            EmailConfirmed = account.EmailVerified,
        };

        if (password is { } value)
        {
            var passwordStore = GetPasswordStore(scope.ServiceProvider);
            var passwordHasher = GetPasswordHasher(userManager);
            var submitted = DecodePassword(value);
            await SetPasswordHashAsync(
                passwordHasher,
                passwordStore,
                user,
                submitted,
                cancellationToken
            );
        }

        var created = await userManager.CreateAsync(user);
        ThrowIfFailed(created, "create the local account");

        foreach (var claim in account.Claims)
        {
            var identityClaim = new Claim(claim.Type, claim.Value);
            var claimAdded = await userManager.AddClaimAsync(user, identityClaim);
            ThrowIfFailed(claimAdded, "add a local-account claim");
        }

        if (!account.IsEnabled)
        {
            await DisableAsync(userManager, user, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask UpdateAsync(
        PersistedLocalAccount account,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();

        var user = await GetRequiredUserAsync(
            userManager,
            account.LocalAccountId,
            cancellationToken
        );

        // SetUserNameAsync/SetEmailAsync also refresh the normalized lookup columns; setting the properties directly
        // would leave those stale. SetEmailAsync resets the confirmed flag, so the confirmation is applied after it.
        var userNameSet = await userManager.SetUserNameAsync(user, account.UserName);
        ThrowIfFailed(userNameSet, "update the username");
        var emailSet = await userManager.SetEmailAsync(user, account.Email);
        ThrowIfFailed(emailSet, "update the email");
        user.EmailConfirmed = account.EmailVerified;

        if (account.IsEnabled)
        {
            await EnableAsync(userManager, user, cancellationToken);
        }
        else
        {
            await DisableAsync(userManager, user, cancellationToken);
        }

        var updated = await userManager.UpdateAsync(user);
        ThrowIfFailed(updated, "update the local account");
    }

    /// <inheritdoc />
    public async ValueTask SetPasswordAsync(
        string localAccountId,
        ReadOnlyMemory<byte> password,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        var passwordStore = GetPasswordStore(scope.ServiceProvider);
        var passwordHasher = GetPasswordHasher(userManager);

        var user = await GetRequiredUserAsync(userManager, localAccountId, cancellationToken);

        var submitted = DecodePassword(password);
        await SetPasswordHashAsync(
            passwordHasher,
            passwordStore,
            user,
            submitted,
            cancellationToken
        );

        // Rotating the security stamp, which also saves the user, invalidates the sessions and tokens issued before the
        // reset.
        var securityStampUpdated = await userManager.UpdateSecurityStampAsync(user);
        ThrowIfFailed(securityStampUpdated, "reset the local-account password");
    }

    /// <inheritdoc />
    public async ValueTask<LocalAccountVerificationResult> VerifyCredentialAsync(
        string userName,
        ReadOnlyMemory<byte> password,
        Func<string, CancellationToken, ValueTask<bool>> isRefusedAsync,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;
        var userManager = serviceProvider.GetRequiredService<UserManager<TUser>>();
        var passwordStore = GetPasswordStore(serviceProvider);
        var passwordHasher = GetPasswordHasher(userManager);

        // Made before the user is looked up, so the first verification costs the same whichever user name it names.
        var dummyPasswordHash = DummyPasswordHash.GetOrCreate(passwordHasher);

        var submitted = DecodePassword(password);

        var user = await FindByNameAsync(userManager, userName, cancellationToken);
        var storedHash = user is null
            ? null
            : await GetVerifiablePasswordHashAsync(
                userManager,
                passwordStore,
                user,
                cancellationToken
            );

        // The limit counts the user name the user holds, and a refused password is verified against the dummy hash, so
        // a refusal costs what a wrong password costs. See ADR-1237.
        var countedUserName = user?.UserName ?? userName;
        var isRefused = await isRefusedAsync(countedUserName, cancellationToken);
        var passwordHash = isRefused ? null : storedHash;

        // An unknown user name, a locked-out user and a user without a password are verified against the dummy hash, so
        // a wrong password takes as long for them as for a user whose hash is verified. See ADR-1239.
        var verifiedUser = user ?? new TUser();
        var verifiedHash = passwordHash ?? dummyPasswordHash;
        var result = passwordHasher.VerifyHashedPassword(verifiedUser, verifiedHash, submitted);
        if (user is null || passwordHash is null || result == PasswordVerificationResult.Failed)
        {
            return new LocalAccountVerificationResult
            {
                UserName = countedUserName,
                IsRefused = isRefused,
            };
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            await SetPasswordHashAsync(
                passwordHasher,
                passwordStore,
                user,
                submitted,
                cancellationToken
            );

            // The credential is unchanged, so the security stamp is kept; when the user validators refuse the update,
            // the stored hash, which still verifies, stays.
            _ = await userManager.UpdateAsync(user);
        }

        var account = await MapAsync(userManager, user, cancellationToken);
        return new LocalAccountVerificationResult { UserName = countedUserName, Account = account };
    }

    /// <inheritdoc />
    public async ValueTask<PersistedLocalAccount?> GetByIdOrDefaultAsync(
        string localAccountId,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();

        var user = await FindByIdAsync(userManager, localAccountId, cancellationToken);
        return user is null ? null : await MapAsync(userManager, user, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<PersistedLocalAccount?> GetByUserNameOrDefaultAsync(
        string userName,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();

        var user = await FindByNameAsync(userManager, userName, cancellationToken);
        return user is null ? null : await MapAsync(userManager, user, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<PersistedLocalAccount>> GetPageAsync(
        PageRequest page,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();

        var query = GetTenantUsers(userManager);

        if (!string.IsNullOrEmpty(page.Cursor))
        {
            var cursor = page.Cursor;
            // Keyset by id; the untyped comparison is translated to a SQL range predicate by the provider (the database
            // collation, not an in-memory ordinal comparison, orders the page), so CA1309 does not apply here.
#pragma warning disable CA1309
            query = query.Where(user => string.Compare(user.Id, cursor) > 0);
#pragma warning restore CA1309
        }

        var limit = page.Limit;

        // Fetch one extra row to determine whether a further page exists.
        var pageQuery = query.OrderBy(user => user.Id).Take(limit + 1);
        var users = await ToListAsync(pageQuery, cancellationToken);

        var hasMore = users.Count > limit;
        var capacity = Math.Min(users.Count, limit);
        var items = new List<PersistedLocalAccount>(capacity);
        for (var index = 0; index < users.Count && index < limit; index++)
        {
            var account = await MapAsync(userManager, users[index], cancellationToken);
            items.Add(account);
        }

        var nextCursor = hasMore ? items[^1].LocalAccountId : null;
        return new PagedResult<PersistedLocalAccount> { Items = items, NextCursor = nextCursor };
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string localAccountId, CancellationToken cancellationToken)
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();

        var user = await FindByIdAsync(userManager, localAccountId, cancellationToken);
        if (user is null)
        {
            return;
        }

        var deleted = await userManager.DeleteAsync(user);
        ThrowIfFailed(deleted, "delete the local account");
    }

    /// <inheritdoc />
    public async ValueTask ReplaceClaimsAsync(
        string localAccountId,
        IReadOnlyList<PersistedLocalAccountClaim> claims,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();

        var user = await GetRequiredUserAsync(userManager, localAccountId, cancellationToken);

        var existing = await userManager.GetClaimsAsync(user);
        if (existing.Count > 0)
        {
            var removed = await userManager.RemoveClaimsAsync(user, existing);
            ThrowIfFailed(removed, "remove the existing local-account claims");
        }

        if (claims.Count > 0)
        {
            var newClaims = claims.Select(claim => new Claim(claim.Type, claim.Value));
            var added = await userManager.AddClaimsAsync(user, newClaims);
            ThrowIfFailed(added, "add the local-account claims");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// ASP.NET Core Identity's claim store reads the claims of one user at a time, so the export reads each user's
    /// claims with a call of its own, one query per user over ASP.NET Core Identity's Entity Framework store. Only a
    /// move exports a tenant's local accounts, while the tenant is disabled, so the cost is accepted.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A stored user of the tenant has no user name, normalized user name or security stamp.
    /// </exception>
    // See ADR-1514.
    public async ValueTask<IReadOnlyList<LocalAccountTransferRecord>> ExportAsync(
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        var passwordStore = GetPasswordStore(scope.ServiceProvider);

        var orderedUsers = GetTenantUsers(userManager).OrderBy(user => user.Id);
        var users = await ToListAsync(orderedUsers, cancellationToken);

        var records = new List<LocalAccountTransferRecord>(users.Count);
        foreach (var user in users)
        {
            var userName = GetRequiredMember(user.UserName, nameof(IdentityUser.UserName), user.Id);
            var normalizedUserName = GetRequiredMember(
                user.NormalizedUserName,
                nameof(IdentityUser.NormalizedUserName),
                user.Id
            );
            var securityStamp = GetRequiredMember(
                user.SecurityStamp,
                nameof(IdentityUser.SecurityStamp),
                user.Id
            );
            var passwordHash = await passwordStore.GetPasswordHashAsync(user, cancellationToken);
            var claims = await userManager.GetClaimsAsync(user);
            var isEnabled =
                !userManager.SupportsUserLockout || !await userManager.IsLockedOutAsync(user);

            var record = new LocalAccountTransferRecord
            {
                LocalAccountId = user.Id,
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                Email = user.Email,
                NormalizedEmail = user.NormalizedEmail,
                EmailVerified = user.EmailConfirmed,
                PasswordHash = passwordHash,
                SecurityStamp = securityStamp,
                IsEnabled = isEnabled,
                LockoutEnabled = user.LockoutEnabled,
                LockoutEnd = user.LockoutEnd,
                AccessFailedCount = user.AccessFailedCount,
                Claims = claims
                    .Select(claim => new PersistedLocalAccountClaim
                    {
                        Type = claim.Type,
                        Value = claim.Value,
                    })
                    .ToList(),
            };
            records.Add(record);
        }

        return records;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The user manager creates each account, which issues a new security stamp, and one update then writes the
    /// record's security stamp, lockout state and claims, so each account takes two writes. ASP.NET Core Identity's
    /// store contracts write one user at a time. Only a move imports a tenant's local accounts, while the tenant is
    /// disabled, so the cost is accepted.
    /// </remarks>
    // See ADR-1514.
    public async ValueTask ImportAsync(
        IReadOnlyList<LocalAccountTransferRecord> records,
        CancellationToken cancellationToken
    )
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();
        var passwordStore = GetPasswordStore(scope.ServiceProvider);
        IUserClaimStore<TUser>? claimStore = null;

        var tenantId = GetRequiredTenantId();
        foreach (var record in records)
        {
            if (!record.IsEnabled)
            {
                ThrowIfLockoutUnsupported(userManager);
            }

            // The user manager derives the normalized names under the ambient tenant, so they are the names the
            // exporting store held for the same tenant.
            var user = new TUser
            {
                Id = record.LocalAccountId,
                TenantId = tenantId,
                UserName = record.UserName,
                Email = record.Email,
                EmailConfirmed = record.EmailVerified,
            };

            if (record.PasswordHash is not null)
            {
                await passwordStore.SetPasswordHashAsync(
                    user,
                    record.PasswordHash,
                    cancellationToken
                );
            }

            var created = await userManager.CreateAsync(user);
            ThrowIfFailed(created, "import the local account");

            // Creating a user issues a new security stamp, so the stamp and the lockout state of the record are
            // applied after it, together with the claims, which the claim store stages on the context the user
            // manager saves.
            user.SecurityStamp = record.SecurityStamp;
            if (userManager.SupportsUserLockout)
            {
                user.LockoutEnabled = record.LockoutEnabled;
                user.LockoutEnd = record.LockoutEnd;
                user.AccessFailedCount = record.AccessFailedCount;
            }

            if (record.Claims.Count > 0)
            {
                claimStore ??= GetClaimStore(scope.ServiceProvider);
                var importedClaims = record.Claims.Select(claim => new Claim(
                    claim.Type,
                    claim.Value
                ));
                await claimStore.AddClaimsAsync(user, importedClaims, cancellationToken);
            }

            var updated = await userManager.UpdateAsync(user);
            ThrowIfFailed(
                updated,
                "import the security stamp, lockout state and claims of the local account"
            );
        }
    }

    /// <inheritdoc />
    public async ValueTask RemoveAllAsync(CancellationToken cancellationToken)
    {
        await using var scope = ServiceScopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TUser>>();

        var tenantUsers = GetTenantUsers(userManager);
        var users = await ToListAsync(tenantUsers, cancellationToken);
        foreach (var user in users)
        {
            var deleted = await userManager.DeleteAsync(user);
            ThrowIfFailed(deleted, "delete the local account");
        }
    }

    /// <summary>
    /// Finds a user by identifier within the ambient tenant, and requires that it exists.
    /// </summary>
    /// <param name="userManager">The <see cref="UserManager{TUser}"/> whose users are queried.</param>
    /// <param name="localAccountId">The identifier of the local account to find.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The user.</returns>
    /// <exception cref="InvalidOperationException">No user of the ambient tenant has the identifier.</exception>
    private async ValueTask<TUser> GetRequiredUserAsync(
        UserManager<TUser> userManager,
        string localAccountId,
        CancellationToken cancellationToken
    ) =>
        await FindByIdAsync(userManager, localAccountId, cancellationToken)
        ?? throw new InvalidOperationException(
            $"A local account with '{localAccountId}' was not found."
        );

    /// <summary>
    /// Gets the tenant that the store stamps and compares, which is the lower-invariant form of the ambient tenant and
    /// the form that <c>TenantLookupNormalizer</c> prefixes lookups with.
    /// </summary>
    /// <returns>The lower-invariant form of the ambient tenant, or <c>null</c> when no tenant scope is set.</returns>
    private string? GetCurrentTenantId() => AmbientTenantAccessor.TenantId?.ToLowerInvariant();

    /// <summary>
    /// Gets the ambient tenant that the store stamps on a new user, which a local account cannot be created without.
    /// </summary>
    /// <returns>The lower-invariant form of the ambient tenant.</returns>
    /// <exception cref="InvalidOperationException">No tenant scope is set.</exception>
    private string GetRequiredTenantId() =>
        GetCurrentTenantId()
        ?? throw new InvalidOperationException(
            "A local account cannot be created without a tenant scope. Open a tenant scope with IAmbientTenantAccessor.BeginScope."
        );

    /// <summary>
    /// Gets the users of the ambient tenant. An unscoped scope reads every tenant's users, and an unset scope reads
    /// none.
    /// </summary>
    /// <param name="userManager">The <see cref="UserManager{TUser}"/> whose users are queried.</param>
    /// <returns>The query over the users that the ambient scope can read, which is empty in an unset scope.</returns>
    /// <exception cref="NotSupportedException">The store does not support querying users.</exception>
    private IQueryable<TUser> GetTenantUsers(UserManager<TUser> userManager)
    {
        if (!userManager.SupportsQueryableUsers)
        {
            throw new NotSupportedException(
                "The configured ASP.NET Core Identity store does not support querying users."
            );
        }

        var tenantId = GetCurrentTenantId();
        if (tenantId is not null)
        {
            return userManager.Users.Where(user => user.TenantId == tenantId);
        }

        return AmbientTenantAccessor.IsUnscoped
            ? userManager.Users
            : userManager.Users.Where(_ => false);
    }

    /// <summary>
    /// Finds a user by identifier. The identifier is matched in the query over the tenant's users, so a user of another
    /// tenant is never read.
    /// </summary>
    /// <param name="userManager">The <see cref="UserManager{TUser}"/> whose users are queried.</param>
    /// <param name="localAccountId">The identifier of the user to find.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The user, or <c>null</c> when no user of the ambient tenant has the identifier or the scope is unset.
    /// </returns>
    /// <exception cref="NotSupportedException">The store does not support querying users.</exception>
    private ValueTask<TUser?> FindByIdAsync(
        UserManager<TUser> userManager,
        string localAccountId,
        CancellationToken cancellationToken
    )
    {
        var matchingUsers = GetTenantUsers(userManager).Where(user => user.Id == localAccountId);
        return FirstOrDefaultAsync(matchingUsers, cancellationToken);
    }

    /// <summary>
    /// Finds a user by user name inside the ambient tenant scope. A user name is unique only within a tenant, so an
    /// unset scope finds nothing, and an unscoped scope is rejected.
    /// </summary>
    /// <param name="userManager">The <see cref="UserManager{TUser}"/> to look the user up with.</param>
    /// <param name="userName">The user name to find.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The user, or <c>null</c> when no scope is set or the user name belongs to no user of the ambient tenant.
    /// </returns>
    /// <exception cref="InvalidOperationException">The ambient scope is unscoped.</exception>
    private async ValueTask<TUser?> FindByNameAsync(
        UserManager<TUser> userManager,
        string userName,
        CancellationToken cancellationToken
    )
    {
        var tenantId = GetCurrentTenantId();
        if (tenantId is null)
        {
            return AmbientTenantAccessor.IsUnscoped
                ? throw new InvalidOperationException(
                    "A local account is found by user name only inside a tenant scope, because a user name is unique only within a tenant."
                )
                : null;
        }

        var user = await userManager.FindByNameAsync(userName);
        return user is not null && string.Equals(user.TenantId, tenantId, StringComparison.Ordinal)
            ? user
            : null;
    }
}
