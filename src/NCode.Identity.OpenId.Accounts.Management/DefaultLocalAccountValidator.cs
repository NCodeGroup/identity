// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Endpoints;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Provides the default implementation of <see cref="ILocalAccountValidator"/>.
/// </summary>
internal class DefaultLocalAccountValidator(IAuthorizationService authorizationService)
    : DefaultResourceValidator(authorizationService),
        ILocalAccountValidator
{
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
    /// Checks that the username is unique within the tenant; a match against a different account (or any account, on
    /// create) conflicts.
    /// </summary>
    /// <param name="storeManager">The <see cref="IStoreManager"/> for the current unit of work.</param>
    /// <param name="userName">The proposed username.</param>
    /// <param name="localAccountId">The identifier of the account being updated, or <c>null</c> on create.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the conflict, or <c>null</c> when the username is free.</returns>
    private static async ValueTask<ManagementError?> CheckUserNameAvailableAsync(
        IStoreManager storeManager,
        string userName,
        string? localAccountId,
        CancellationToken cancellationToken
    )
    {
        var existing = await storeManager
            .GetStore<ILocalAccountStore>()
            .GetByUserNameOrDefaultAsync(userName, cancellationToken);

        if (
            existing is not null
            && !string.Equals(existing.LocalAccountId, localAccountId, StringComparison.Ordinal)
        )
        {
            return new ManagementError
            {
                StatusCode = StatusCodes.Status409Conflict,
                Detail = "A local account with the specified username already exists.",
            };
        }

        return null;
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
    public ValueTask<ManagementError?> ValidateCreateAsync(
        PersistedLocalAccount account,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        if (GetDisplayNameErrorOrDefault(account.DisplayName) is { } displayNameError)
        {
            return ValueTask.FromResult<ManagementError?>(displayNameError);
        }

        return CheckUserNameAvailableAsync(
            storeManager,
            account.UserName,
            localAccountId: null,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public ValueTask<ManagementError?> ValidateUpdateAsync(
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
            return ValueTask.FromResult<ManagementError?>(preconditionError);
        }

        if (GetDisplayNameErrorOrDefault(model.DisplayName) is { } displayNameError)
        {
            return ValueTask.FromResult<ManagementError?>(displayNameError);
        }

        return CheckUserNameAvailableAsync(
            storeManager,
            model.UserName,
            account.LocalAccountId,
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
