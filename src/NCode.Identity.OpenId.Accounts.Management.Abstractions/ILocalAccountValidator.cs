// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.Management;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Validates the intrinsic core preconditions for local-account management operations. The <c>Authorize*</c> members
/// judge the caller from the route values before any row is read, and the <c>Validate*</c> members judge the row.
/// Register a custom implementation to replace or extend the default validation.
/// </summary>
[PublicAPI]
public interface ILocalAccountValidator
{
    /// <summary>
    /// Authorizes the caller to create local accounts in the specified tenant before any row is read.
    /// </summary>
    /// <param name="user">The <see cref="ClaimsPrincipal"/> for the current caller.</param>
    /// <param name="tenantId">The identifier of the tenant the account is created in.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the refusal, or <c>null</c> when the caller is authorized.</returns>
    ValueTask<ManagementError?> AuthorizeCreateAsync(
        ClaimsPrincipal user,
        string tenantId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Authorizes the caller to update local accounts in the specified tenant before any row is read.
    /// </summary>
    /// <param name="user">The <see cref="ClaimsPrincipal"/> for the current caller.</param>
    /// <param name="tenantId">The identifier of the tenant the account belongs to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the refusal, or <c>null</c> when the caller is authorized.</returns>
    ValueTask<ManagementError?> AuthorizeUpdateAsync(
        ClaimsPrincipal user,
        string tenantId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Authorizes the caller to delete local accounts in the specified tenant before any row is read.
    /// </summary>
    /// <param name="user">The <see cref="ClaimsPrincipal"/> for the current caller.</param>
    /// <param name="tenantId">The identifier of the tenant the account belongs to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the refusal, or <c>null</c> when the caller is authorized.</returns>
    ValueTask<ManagementError?> AuthorizeDeleteAsync(
        ClaimsPrincipal user,
        string tenantId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Validates that the staged local account may be created. The default implementation reports 400 when the username
    /// is empty or longer than <see cref="AccountMaxLengths.UserName"/>, or when the display name is empty or longer
    /// than <see cref="AccountMaxLengths.DisplayName"/>, and 409 when another account holds the username.
    /// </summary>
    /// <param name="account">The staged <see cref="PersistedLocalAccount"/> to be created, its display name defaulted
    /// and trimmed.</param>
    /// <param name="storeManager">The <see cref="IStoreManager"/> for the current unit of work.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the first failed precondition, or <c>null</c> when valid.</returns>
    ValueTask<ManagementError?> ValidateCreateAsync(
        PersistedLocalAccount account,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Validates that the specified local account may be updated. The default implementation reports 400 when the
    /// username is empty or longer than <see cref="AccountMaxLengths.UserName"/>, or when the display name is empty or
    /// longer than <see cref="AccountMaxLengths.DisplayName"/>, and 409 when another account holds the username.
    /// </summary>
    /// <param name="account">The current <see cref="PersistedLocalAccount"/> being updated.</param>
    /// <param name="model">The proposed (patched) profile, its display name trimmed.</param>
    /// <param name="ifMatch">The optional <c>If-Match</c> precondition: <c>*</c>, a quoted strong entity tag or a comma-separated list of them, or the bare concurrency token.</param>
    /// <param name="storeManager">The <see cref="IStoreManager"/> for the current unit of work.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the first failed precondition, or <c>null</c> when valid.</returns>
    ValueTask<ManagementError?> ValidateUpdateAsync(
        PersistedLocalAccount account,
        UpdateLocalAccountRequest model,
        string? ifMatch,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Validates that the specified local account may be deleted.
    /// </summary>
    /// <param name="account">The current <see cref="PersistedLocalAccount"/> being deleted.</param>
    /// <param name="storeManager">The <see cref="IStoreManager"/> for the current unit of work.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the first failed precondition, or <c>null</c> when valid.</returns>
    ValueTask<ManagementError?> ValidateDeleteAsync(
        PersistedLocalAccount account,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    );
}
