// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// The tenant, the connection and the local account a route names.
/// </summary>
/// <param name="TenantId">The identifier of the tenant.</param>
/// <param name="ConnectionId">The identifier of the connection.</param>
/// <param name="LocalAccountId">The identifier of the local account.</param>
internal readonly record struct AccountRoute(
    string TenantId,
    string ConnectionId,
    string LocalAccountId
);
