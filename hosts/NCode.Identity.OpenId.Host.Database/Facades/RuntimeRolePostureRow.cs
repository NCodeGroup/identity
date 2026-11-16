// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Host.Database.Facades;

/// <summary>
/// The facts about a Postgres role that decide whether row-level security applies to it.
/// </summary>
/// <param name="IsSuperuser">Whether the role is a superuser.</param>
/// <param name="BypassesRls">Whether the role has <c>BYPASSRLS</c>.</param>
/// <param name="OwnsTenantTables">Whether the role owns tables in the current schema.</param>
internal readonly record struct RuntimeRolePostureRow(
    bool IsSuperuser,
    bool BypassesRls,
    bool OwnsTenantTables
);
