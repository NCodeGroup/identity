// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Endpoints;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Provides the default implementation of <see cref="ILocalAccountValidator"/>.
/// </summary>
internal class DefaultLocalAccountValidator(
    IAuthorizationService authorizationService,
    LocalAccountPasswords localAccountPasswords,
    LocalAccountStoreCatalog localAccountStoreCatalog
) : DefaultResourceValidator(authorizationService), ILocalAccountValidator
{
    private LocalAccountPasswords LocalAccountPasswords { get; } = localAccountPasswords;
    private LocalAccountStoreCatalog LocalAccountStoreCatalog { get; } = localAccountStoreCatalog;

    /// <summary>
    /// Checks the identifiers the connection's <c>local_account_identifier</c> setting demands, and bounds the ones the
    /// account carries.
    /// </summary>
    /// <param name="mode">The connection's <c>local_account_identifier</c> value.</param>
    /// <param name="userName">The proposed username, or <c>null</c>.</param>
    /// <param name="email">The proposed email address, or <c>null</c>.</param>
    /// <returns>A <see cref="ManagementError"/> describing the refusal, or <c>null</c> when valid.</returns>
    private static ManagementError? GetIdentifierErrorOrDefault(
        string mode,
        string? userName,
        string? email
    )
    {
        var hasUserName = !string.IsNullOrWhiteSpace(userName);
        var hasEmail = !string.IsNullOrWhiteSpace(email);

        var detail = mode switch
        {
            LocalAccountIdentifiers.Email when !hasEmail => "The email address is required.",
            LocalAccountIdentifiers.UserName when !hasUserName => "The username is required.",
            LocalAccountIdentifiers.EmailOrUserName when !hasUserName && !hasEmail =>
                "A username or an email address is required.",
            _ => null,
        };

        if (detail is null && userName is { Length: > AccountMaxLengths.UserName })
        {
            detail = $"The username must be at most {AccountMaxLengths.UserName} characters.";
        }

        if (detail is null && email is { Length: > AccountMaxLengths.Email })
        {
            detail = $"The email address must be at most {AccountMaxLengths.Email} characters.";
        }

        return detail is null
            ? null
            : new ManagementError { StatusCode = StatusCodes.Status400BadRequest, Detail = detail };
    }

    /// <summary>
    /// Checks the display name, which is the source of the account's name claim, so it is required and bounded.
    /// </summary>
    /// <param name="displayName">The proposed display name, already trimmed.</param>
    /// <returns>A <see cref="ManagementError"/> describing the refusal, or <c>null</c> when valid.</returns>
    private static ManagementError? GetDisplayNameErrorOrDefault(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return new ManagementError
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Detail = "The display name is required.",
            };
        }

        if (displayName.Length > AccountMaxLengths.DisplayName)
        {
            return new ManagementError
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Detail =
                    $"The display name must be at most {AccountMaxLengths.DisplayName} characters.",
            };
        }

        return null;
    }

    /// <summary>
    /// Checks that the username and the email address are unique within the connection; a match against a different
    /// account (or any account, on create) conflicts.
    /// </summary>
    /// <param name="store">The <see cref="ILocalAccountStore"/> of the connection.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="userName">The proposed username, or <c>null</c>.</param>
    /// <param name="email">The proposed email address, or <c>null</c>.</param>
    /// <param name="localAccountId">The identifier of the account being updated, or <c>null</c> on create.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the conflict, or <c>null</c> when both are free.</returns>
    private static async ValueTask<ManagementError?> CheckIdentifiersAvailableAsync(
        ILocalAccountStore store,
        string connectionId,
        string? userName,
        string? email,
        string? localAccountId,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(userName))
        {
            var existing = await store.GetByUserNameOrDefaultAsync(
                connectionId,
                userName,
                cancellationToken
            );
            if (IsOtherAccount(existing, localAccountId))
            {
                return new ManagementError
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    Detail = "A local account with the specified username already exists.",
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var existing = await store.GetByEmailOrDefaultAsync(
                connectionId,
                email,
                cancellationToken
            );
            if (IsOtherAccount(existing, localAccountId))
            {
                return new ManagementError
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    Detail = "A local account with the specified email address already exists.",
                };
            }
        }

        // An identifier of one field never equals another account's identifier of the other field, so a sign-in
        // identifier names one account whatever the connection's local_account_identifier is later set to.
        if (!string.IsNullOrWhiteSpace(userName))
        {
            var shadowed = await store.GetByEmailOrDefaultAsync(
                connectionId,
                userName,
                cancellationToken
            );
            if (IsOtherAccount(shadowed, localAccountId))
            {
                return new ManagementError
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    Detail =
                        "The specified username is the email address of another local account.",
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var shadowed = await store.GetByUserNameOrDefaultAsync(
                connectionId,
                email,
                cancellationToken
            );
            if (IsOtherAccount(shadowed, localAccountId))
            {
                return new ManagementError
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    Detail =
                        "The specified email address is the username of another local account.",
                };
            }
        }

        return null;
    }

    private static bool IsOtherAccount(PersistedLocalAccount? existing, string? localAccountId)
    {
        return existing is not null
            && !string.Equals(existing.LocalAccountId, localAccountId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks the account's identifiers against the connection's settings and its store.
    /// </summary>
    private async ValueTask<ManagementError?> ValidateIdentifiersAsync(
        PersistedConnection connection,
        string tenantId,
        string? userName,
        string? email,
        string? localAccountId,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var (settings, error) = await LocalAccountPasswords.ResolveConnectionSettingsAsync(
            storeManager,
            tenantId,
            connection,
            cancellationToken
        );
        if (settings is null)
        {
            return error;
        }

        var mode = settings.GetValue(OpenIdSettingKeys.LocalAccountIdentifier);
        var identifierError = GetIdentifierErrorOrDefault(mode, userName, email);
        if (identifierError is not null)
        {
            return identifierError;
        }

        var store = LocalAccountStoreCatalog.GetStore(storeManager, connection);
        return await CheckIdentifiersAvailableAsync(
            store,
            connection.ConnectionId,
            userName,
            email,
            localAccountId,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<ManagementError?> AuthorizeCreateAsync(
        ClaimsPrincipal user,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var resource = TenantScopeResource.For(tenantId);
        return AuthorizeAsync(
            user,
            resource,
            Operations.Create,
            "The caller is not authorized to create local accounts in this tenant.",
            "Authentication is required to create local accounts in this tenant.",
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<ManagementError?> AuthorizeUpdateAsync(
        ClaimsPrincipal user,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var resource = TenantScopeResource.For(tenantId);
        return AuthorizeAsync(
            user,
            resource,
            Operations.Update,
            "The caller is not authorized to update this local account.",
            "Authentication is required to update this local account.",
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<ManagementError?> AuthorizeDeleteAsync(
        ClaimsPrincipal user,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var resource = TenantScopeResource.For(tenantId);
        return AuthorizeAsync(
            user,
            resource,
            Operations.Delete,
            "The caller is not authorized to delete this local account.",
            "Authentication is required to delete this local account.",
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask<ManagementError?> ValidateCreateAsync(
        PersistedConnection connection,
        PersistedLocalAccount account,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var identifierError = await ValidateIdentifiersAsync(
            connection,
            account.TenantId,
            account.UserName,
            account.Email,
            localAccountId: null,
            storeManager,
            cancellationToken
        );
        if (identifierError is not null)
        {
            return identifierError;
        }

        return GetDisplayNameErrorOrDefault(account.DisplayName);
    }

    /// <inheritdoc />
    public async ValueTask<ManagementError?> ValidateUpdateAsync(
        PersistedConnection connection,
        PersistedLocalAccount account,
        UpdateLocalAccountRequest model,
        string? ifMatch,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        if (
            ManagementEntityTags.GetIfMatchErrorOrDefault(
                ifMatch,
                account.ConcurrencyToken,
                "local account"
            ) is
            { } preconditionError
        )
        {
            return preconditionError;
        }

        if (GetDisplayNameErrorOrDefault(model.DisplayName) is { } displayNameError)
        {
            return displayNameError;
        }

        return await ValidateIdentifiersAsync(
            connection,
            account.TenantId,
            model.UserName,
            model.Email,
            account.LocalAccountId,
            storeManager,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<ManagementError?> ValidateDeleteAsync(
        PersistedLocalAccount account,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<ManagementError?>(null);
}
