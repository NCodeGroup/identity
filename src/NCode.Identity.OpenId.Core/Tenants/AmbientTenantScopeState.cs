// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Tenants;

/// <summary>
/// The state of one ambient tenant scope, which is a tenant, or unscoped with its reason.
/// </summary>
/// <param name="TenantId">The identifier of the tenant the scope reads, or <see langword="null"/> when unscoped.</param>
/// <param name="IsUnscoped">Whether the scope reads across every tenant.</param>
/// <param name="Reason">Why the scope reads across every tenant, or <see langword="null"/> for a tenant scope.</param>
internal sealed record AmbientTenantScopeState(string? TenantId, bool IsUnscoped, string? Reason);
