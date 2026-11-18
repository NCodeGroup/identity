// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Transfers;

/// <summary>
/// Defines the <see cref="ITenantTransferContributor.Order"/> of each built-in contributor. A contributor whose rows
/// name another's takes a later position.
/// </summary>
// See ADR-1514.
[PublicAPI]
public static class TenantTransferOrders
{
    /// <summary>
    /// The position of the contributor that carries the tenant row, its settings, clients, grants, resource servers and
    /// secrets, which every later section's rows name.
    /// </summary>
    public const int Core = 0;

    /// <summary>
    /// The position of the contributor that carries the local accounts, which follow the core section.
    /// </summary>
    public const int LocalAccounts = 100;
}
