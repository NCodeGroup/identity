// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Persistence.Tenants;

/// <summary>
/// Holds the ambient tenant scope for the current request so that tenant-bound data access can be constrained to a
/// single tenant. When a tenant scope is active, the persistence layer only returns rows owned by
/// <see cref="TenantId"/>, so a resource belonging to another tenant is never materialized (defense against
/// cross-tenant data leaks).
/// </summary>
/// <remarks>
/// <para>
/// The scope has three states: unset, tenant (<see cref="BeginScope"/>) and unscoped (<see cref="BeginUnscoped"/>).
/// Tenant-bound data access in the unset state matches no rows, and code that reads across tenants opens an unscoped
/// scope with a reason. A tenant scope opened inside an unscoped scope is a tenant scope.
/// </para>
/// <para>
/// The scope flows across asynchronous calls (including data-access components created outside the current dependency
/// injection scope), so it is safe to establish once per request and read from anywhere within that request.
/// </para>
/// </remarks>
[PublicAPI]
public interface IAmbientTenantAccessor
{
    /// <summary>
    /// Gets a value indicating whether an ambient tenant scope is currently active. When <c>false</c>, the state is
    /// either unset or unscoped (see <see cref="IsUnscoped"/>).
    /// </summary>
    bool IsScoped { get; }

    /// <summary>
    /// Gets the identifier of the ambient tenant for the current request, or <c>null</c> when no tenant scope is
    /// active.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Gets a value indicating whether an unscoped scope is currently active, which lets tenant-bound data access
    /// read across tenants. It is <c>false</c> inside a tenant scope, even when that scope is nested in an unscoped
    /// one.
    /// </summary>
    bool IsUnscoped { get; }

    /// <summary>
    /// Gets the reason given to <see cref="BeginUnscoped"/> while an unscoped scope is active, or <c>null</c>
    /// otherwise.
    /// </summary>
    string? UnscopedReason { get; }

    /// <summary>
    /// Establishes an ambient tenant scope for the current request. The scope remains active until the returned
    /// <see cref="IDisposable"/> is disposed, which restores the previous scope.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant to scope tenant-bound data access to.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous scope when disposed.</returns>
    IDisposable BeginScope(string tenantId);

    /// <summary>
    /// Establishes an unscoped scope in which tenant-bound data access reads across tenants. The scope remains active
    /// until the returned <see cref="IDisposable"/> is disposed, which restores the previous scope.
    /// </summary>
    /// <param name="reason">The reason the code reads across tenants. It must not be blank.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous scope when disposed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is blank.</exception>
    IDisposable BeginUnscoped(string reason);
}
