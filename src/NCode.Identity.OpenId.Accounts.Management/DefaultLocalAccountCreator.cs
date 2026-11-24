// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Provides the default implementation of <see cref="ILocalAccountCreator"/>.
/// </summary>
internal sealed class DefaultLocalAccountCreator(
    ILocalAccountValidator localAccountValidator,
    ICryptoService cryptoService,
    LocalAccountPasswords localAccountPasswords,
    LocalAccountStoreCatalog localAccountStoreCatalog
) : ILocalAccountCreator
{
    private ILocalAccountValidator LocalAccountValidator { get; } = localAccountValidator;
    private ICryptoService CryptoService { get; } = cryptoService;
    private LocalAccountPasswords LocalAccountPasswords { get; } = localAccountPasswords;
    private LocalAccountStoreCatalog LocalAccountStoreCatalog { get; } = localAccountStoreCatalog;

    /// <inheritdoc />
    public async ValueTask<LocalAccountCreation> StageAsync(
        IStoreManager storeManager,
        PersistedConnection connection,
        CreateLocalAccountRequest request,
        CancellationToken cancellationToken
    )
    {
        var claimTypes = (request.Claims ?? []).Select(claim => claim.Type);
        var reservedClaimError = LocalAccountClaimRules.GetReservedClaimErrorOrDefault(claimTypes);
        if (reservedClaimError is not null)
        {
            return new LocalAccountCreation { Error = reservedClaimError };
        }

        var userName = string.IsNullOrWhiteSpace(request.UserName) ? null : request.UserName;
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email;
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? userName ?? email ?? string.Empty
            : request.DisplayName.Trim();

        var localAccountId = CryptoService.GenerateResourceId();
        var principalId = CryptoService.GenerateResourceId();

        var account = new PersistedLocalAccount
        {
            TenantId = connection.TenantId,
            LocalAccountId = localAccountId,
            ConnectionId = connection.ConnectionId,
            DisplayName = displayName,
            UserName = userName,
            Email = email,
            EmailVerified = request.EmailVerified,
            IsEnabled = request.IsEnabled,
            Claims =
                request
                    .Claims?.Select(claim => new PersistedLocalAccountClaim
                    {
                        Type = claim.Type,
                        Value = claim.Value,
                    })
                    .ToList()
                ?? [],
            ConcurrencyToken = string.Empty,
        };

        var error = await LocalAccountValidator.ValidateCreateAsync(
            connection,
            account,
            storeManager,
            cancellationToken
        );
        if (error is not null)
        {
            return new LocalAccountCreation { Error = error };
        }

        var passwordError = await LocalAccountPasswords.ValidateAsync(
            storeManager,
            connection,
            account,
            request.Password,
            cancellationToken
        );
        if (passwordError is not null)
        {
            return new LocalAccountCreation { Error = passwordError };
        }

        // Eagerly create the owning principal and the account's identity in its connection so the account resolves to a
        // principal uniformly with any federated identity (ADR-1051/ADR-1057).
        var principal = new PersistedFederatedPrincipal
        {
            TenantId = connection.TenantId,
            PrincipalId = principalId,
            ConcurrencyToken = string.Empty,
        };

        // Set-when-present: a missing bag leaves the principal's default empty object (ADR-1055).
        if (request.ProfileMetadata is { } profileMetadata)
        {
            principal.ProfileMetadata = profileMetadata;
        }

        if (request.SystemMetadata is { } systemMetadata)
        {
            principal.SystemMetadata = systemMetadata;
        }

        await storeManager
            .GetStore<IFederatedPrincipalStore>()
            .AddAsync(principal, cancellationToken);

        var identity = new PersistedFederatedIdentity
        {
            TenantId = connection.TenantId,
            FederatedIdentityId = CryptoService.GenerateResourceId(),
            PrincipalId = principalId,
            ConnectionId = connection.ConnectionId,
            Issuer = null,
            Subject = localAccountId,
            JoinKey = null,
            ConcurrencyToken = string.Empty,
        };
        await storeManager
            .GetStore<IFederatedIdentityStore>()
            .AddAsync(identity, cancellationToken);

        var localAccountStore = LocalAccountStoreCatalog.GetStore(storeManager, connection);
        await LocalAccountPasswords.WithPasswordBytesAsync(
            request.Password,
            (bytes, ct) => localAccountStore.AddAsync(account, bytes, ct),
            cancellationToken
        );

        return new LocalAccountCreation { PrincipalId = principalId, Account = account };
    }
}
