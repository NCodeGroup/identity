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
    /// account carries and the request changes.
    /// </summary>
    /// <param name="mode">The connection's <c>local_account_identifier</c> value.</param>
    /// <param name="change">The proposed identifiers, the current account and which identifiers change.</param>
    /// <returns>A <see cref="ManagementError"/> describing the refusal, or <c>null</c> when valid.</returns>
    private static ManagementError? GetIdentifierErrorOrDefault(
        string mode,
        IdentifierChange change
    )
    {
        var (proposal, current, userNameChanged, emailChanged) = change;
        // The mode is answered unless the account's stored identifiers already fail it and the request removes none of
        // them, so such an account can still be renamed or disabled.
        var detail = GetModeDetailOrDefault(mode, proposal.UserName, proposal.Email);
        if (detail is not null && current is not null)
        {
            var removesIdentifier =
                (
                    string.IsNullOrWhiteSpace(proposal.UserName)
                    && !string.IsNullOrWhiteSpace(current.UserName)
                )
                || (
                    string.IsNullOrWhiteSpace(proposal.Email)
                    && !string.IsNullOrWhiteSpace(current.Email)
                );
            var storedDetail = GetModeDetailOrDefault(mode, current.UserName, current.Email);
            if (storedDetail is not null && !removesIdentifier)
            {
                detail = null;
            }
        }

        if (
            detail is null
            && userNameChanged
            && proposal.UserName is { Length: > AccountMaxLengths.UserName }
        )
        {
            detail = $"The username must be at most {AccountMaxLengths.UserName} characters.";
        }

        if (
            detail is null
            && emailChanged
            && proposal.Email is { Length: > AccountMaxLengths.Email }
        )
        {
            detail = $"The email address must be at most {AccountMaxLengths.Email} characters.";
        }

        return detail is null
            ? null
            : new ManagementError { StatusCode = StatusCodes.Status400BadRequest, Detail = detail };
    }

    /// <summary>
    /// Checks the identifiers against the connection's <c>local_account_identifier</c> mode.
    /// </summary>
    /// <param name="mode">The connection's <c>local_account_identifier</c> value.</param>
    /// <param name="userName">The username to check.</param>
    /// <param name="email">The email address to check.</param>
    /// <returns>The message describing the identifier the mode requires, or <c>null</c> when the mode is
    /// satisfied.</returns>
    private static string? GetModeDetailOrDefault(string mode, string? userName, string? email)
    {
        var hasUserName = !string.IsNullOrWhiteSpace(userName);
        var hasEmail = !string.IsNullOrWhiteSpace(email);

        return mode switch
        {
            LocalAccountIdentifiers.Email when !hasEmail => "The email address is required.",
            LocalAccountIdentifiers.UserName when !hasUserName => "The username is required.",
            LocalAccountIdentifiers.EmailOrUserName when !hasUserName && !hasEmail =>
                "A username or an email address is required.",
            _ => null,
        };
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
    /// Checks that the username and the email address are unique within the connection and that neither is the
    /// identifier of the other field on another account; a match against a different account (or any account, on
    /// create) conflicts.
    /// </summary>
    /// <param name="store">The <see cref="ILocalAccountStore"/> of the connection.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="proposal">The <see cref="LocalAccountIdentifierProposal"/> to check.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the conflict, or <c>null</c> when both are free.</returns>
    private static async ValueTask<ManagementError?> CheckIdentifiersAvailableAsync(
        ILocalAccountStore store,
        string connectionId,
        LocalAccountIdentifierProposal proposal,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(proposal.UserName))
        {
            var existing = await store.GetByUserNameOrDefaultAsync(
                connectionId,
                proposal.UserName,
                cancellationToken
            );
            if (IsOtherAccount(existing, proposal.LocalAccountId))
            {
                return new ManagementError
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    Detail = "A local account with the specified username already exists.",
                };
            }

            // An email-only account is stored under its identifier as the user name, so no user name equals an
            // account's identifier.
            var byId = await store.GetByIdOrDefaultAsync(proposal.UserName, cancellationToken);
            if (byId is not null)
            {
                return new ManagementError
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    Detail = "The specified username is the identifier of a local account.",
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(proposal.Email))
        {
            var existing = await store.GetByEmailOrDefaultAsync(
                connectionId,
                proposal.Email,
                cancellationToken
            );
            if (IsOtherAccount(existing, proposal.LocalAccountId))
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
        var shadowing = await LocalAccountIdentifierShadowing.FindAsync(
            store,
            connectionId,
            proposal,
            cancellationToken
        );
        if (shadowing.UserNameIsAnEmail)
        {
            return new ManagementError
            {
                StatusCode = StatusCodes.Status409Conflict,
                Detail = "The specified username is the email address of another local account.",
            };
        }

        if (shadowing.EmailIsAUserName)
        {
            return new ManagementError
            {
                StatusCode = StatusCodes.Status409Conflict,
                Detail = "The specified email address is the username of another local account.",
            };
        }

        return null;
    }

    /// <summary>
    /// Determines whether a lookup found an account other than the one being checked.
    /// </summary>
    /// <param name="existing">The account the lookup found, or <c>null</c> when none matched.</param>
    /// <param name="localAccountId">The identifier of the account being checked, or <c>null</c> on create.</param>
    /// <returns><c>true</c> when <paramref name="existing"/> is an account with a different identifier; otherwise,
    /// <c>false</c>.</returns>
    private static bool IsOtherAccount(PersistedLocalAccount? existing, string? localAccountId)
    {
        return existing is not null
            && !string.Equals(existing.LocalAccountId, localAccountId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks the account's identifiers against the connection's settings: which are required and how long they may be.
    /// </summary>
    private async ValueTask<ManagementError?> ValidateIdentifierRequirementsAsync(
        PersistedConnection connection,
        IdentifierChange change,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var (settings, error) = await LocalAccountPasswords.ResolveConnectionSettingsAsync(
            storeManager,
            connection,
            cancellationToken
        );
        if (settings is null)
        {
            return error;
        }

        var mode = settings.GetValue(OpenIdSettingKeys.LocalAccountIdentifier);
        return GetIdentifierErrorOrDefault(mode, change);
    }

    /// <summary>
    /// Checks the account's identifiers against the connection's store for conflicts.
    /// </summary>
    private ValueTask<ManagementError?> ValidateIdentifiersAvailableAsync(
        PersistedConnection connection,
        LocalAccountIdentifierProposal proposal,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var store = LocalAccountStoreCatalog.GetStore(storeManager, connection);
        return CheckIdentifiersAvailableAsync(
            store,
            connection.ConnectionId,
            proposal,
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
        var proposal = new LocalAccountIdentifierProposal
        {
            UserName = account.UserName,
            Email = account.Email,
        };
        var change = IdentifierChange.ForCreate(proposal);
        var requirementError = await ValidateIdentifierRequirementsAsync(
            connection,
            change,
            storeManager,
            cancellationToken
        );
        if (requirementError is not null)
        {
            return requirementError;
        }

        if (GetDisplayNameErrorOrDefault(account.DisplayName) is { } displayNameError)
        {
            return displayNameError;
        }

        return await ValidateIdentifiersAvailableAsync(
            connection,
            proposal,
            storeManager,
            cancellationToken
        );
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

        // Only an identifier the patch changes is checked, so an account whose identifiers no longer fit its
        // connection's mode can still be disabled or renamed.
        var proposal = new LocalAccountIdentifierProposal
        {
            UserName = model.UserName,
            Email = model.Email,
            LocalAccountId = account.LocalAccountId,
        };
        var change = IdentifierChange.ForUpdate(proposal, account);
        if (!change.UserNameChanged && !change.EmailChanged)
        {
            return null;
        }

        var requirementError = await ValidateIdentifierRequirementsAsync(
            connection,
            change,
            storeManager,
            cancellationToken
        );
        if (requirementError is not null)
        {
            return requirementError;
        }

        // The store lookups cover only the identifiers the patch changes.
        var changedProposal = new LocalAccountIdentifierProposal
        {
            UserName = change.UserNameChanged ? model.UserName : null,
            Email = change.EmailChanged ? model.Email : null,
            LocalAccountId = account.LocalAccountId,
        };
        return await ValidateIdentifiersAvailableAsync(
            connection,
            changedProposal,
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
