// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Validates a local-account creation request and stages the account on a store manager. The management create
/// endpoint and every other caller that creates a local account share this one implementation. Register a custom
/// implementation to replace or extend the default behavior.
/// </summary>
[PublicAPI]
public interface ILocalAccountCreator
{
    /// <summary>
    /// Refuses a <c>name</c> profile claim with 400, because the display name is the source of that claim, defaults the
    /// display name to the username, else the email address, when the request carries none, stages the local account
    /// and validates it with <see cref="ILocalAccountValidator.ValidateCreateAsync"/>, and checks the password against
    /// the connection's password policy. Only then does it stage the owning federated principal (with the request's
    /// profile and system metadata, when present), the account's federated identity in the connection and the local
    /// account (with its claims and password hash) in the connection's store on the store manager. The method does not
    /// save: the caller saves the store manager to persist the staged rows, alone or together with other changes in the
    /// same unit of work. Nothing is staged when <see cref="LocalAccountCreation.Error"/> is set.
    /// </summary>
    /// <param name="storeManager">The <see cref="IStoreManager"/> that reads the existing accounts and the tenant and receives the staged rows.</param>
    /// <param name="tenantId">The identifier of the tenant the account is created in.</param>
    /// <param name="connection">The local <see cref="PersistedConnection"/> that holds the account.</param>
    /// <param name="request">The <see cref="CreateLocalAccountRequest"/> describing the account.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A <see cref="LocalAccountCreation"/> carrying the staged principal identifier and account, or, when a check
    /// refuses the request, a <see cref="LocalAccountCreation"/> whose <see cref="LocalAccountCreation.Error"/> holds
    /// the first refusal. The default implementation reports 409 when the username or email address is taken in the
    /// connection, 404 when the tenant is not found and 400 when the connection's settings require an identifier the
    /// request lacks or its password policy rejects the password.
    /// </returns>
    ValueTask<LocalAccountCreation> StageAsync(
        IStoreManager storeManager,
        string tenantId,
        PersistedConnection connection,
        CreateLocalAccountRequest request,
        CancellationToken cancellationToken
    );
}
