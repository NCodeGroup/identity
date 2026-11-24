// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// The tenant and the connection a route names; the connection is absent for a platform route, which operates on the
/// control-plane tenant's default connection.
/// </summary>
/// <param name="TenantId">The identifier of the tenant.</param>
/// <param name="ConnectionId">The identifier of the connection, or <c>null</c> for a platform route.</param>
internal readonly record struct ConnectionRoute(string TenantId, string? ConnectionId);
