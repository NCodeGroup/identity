// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.AspNetCore.Identity;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

/// <summary>
/// Normalizes the names and emails that ASP.NET Core Identity looks users up by, and prefixes each with the
/// normalized identifier of the ambient tenant (<c>{normalizedTenantId}|{value}</c>), so a user name or email is
/// unique per tenant and a lookup in one tenant never finds a user of another.
/// </summary>
/// <remarks>
/// The normalizer fails closed. It throws when no tenant scope is set, and it returns the plain upper-invariant value
/// inside an unscoped scope (<see cref="IAmbientTenantAccessor.IsUnscoped"/>), where code reads across tenants.
/// </remarks>
/// <param name="ambientTenantAccessor">The ambient tenant accessor that supplies the tenant of the current scope.</param>
[PublicAPI]
public sealed class TenantLookupNormalizer(IAmbientTenantAccessor ambientTenantAccessor)
    : ILookupNormalizer
{
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public string? NormalizeName(string? name) => Normalize(name);

    /// <inheritdoc />
    public string? NormalizeEmail(string? email) => Normalize(email);

    private string? Normalize(string? value)
    {
        var tenantId = AmbientTenantAccessor.TenantId;
        if (tenantId is null && !AmbientTenantAccessor.IsUnscoped)
        {
            throw new InvalidOperationException(
                "A user name or email cannot be normalized without a tenant scope. Open a tenant scope with IAmbientTenantAccessor.BeginScope, or an unscoped scope with BeginUnscoped."
            );
        }

        var upper = value?.Normalize().ToUpperInvariant();
        if (tenantId is null || upper is null)
        {
            return upper;
        }

        return $"{tenantId.ToLowerInvariant()}|{upper}";
    }
}
