// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// The values that a route which enables or disables a local account loads before it writes: the open store manager,
/// the store that holds the account, the account and the principal behind its identity. A refused request carries only
/// <paramref name="Refusal"/>: the other members are unset when it is set, and nothing reads them.
/// </summary>
/// <param name="Refusal">The result that answers the request when it is refused, or <see langword="null"/> when the
/// account is loaded.</param>
/// <param name="StoreManager">The open store manager, which the caller disposes.</param>
/// <param name="Store">The store that holds the account.</param>
/// <param name="Account">The local account.</param>
/// <param name="PrincipalId">The identifier of the principal behind the account's identity, or <see langword="null"/>
/// when the account has no identity.</param>
internal readonly record struct AccountChangeTarget(
    IResult? Refusal,
    IStoreManager StoreManager,
    ILocalAccountStore Store,
    PersistedLocalAccount Account,
    string? PrincipalId
);
