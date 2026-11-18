// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.EntityFramework;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// The ambient tenant accessor of an <see cref="OpenIdDbContext"/> that is constructed outside a request, by the EF
/// Core design-time tools and the migration runner. It is always unscoped, with the reason <c>schema migration</c>, so
/// the context's query filters and, through the <see cref="TenantSessionInterceptor"/>, its Postgres session read every
/// tenant's rows; it begins no other scope.
/// </summary>
// See ADR-1018.
internal class UnscopedAmbientTenantAccessor : IAmbientTenantAccessor
{
    /// <inheritdoc />
    public bool IsScoped => false;

    /// <inheritdoc />
    public string? TenantId => null;

    /// <inheritdoc />
    public bool IsUnscoped => true;

    /// <inheritdoc />
    public string? UnscopedReason => "schema migration";

    /// <inheritdoc />
    public IDisposable BeginScope(string tenantId) =>
        throw new NotSupportedException("Unscoped construction cannot begin a tenant scope.");

    /// <inheritdoc />
    public IDisposable BeginUnscoped(string reason) =>
        throw new NotSupportedException("Unscoped construction cannot begin another scope.");
}
