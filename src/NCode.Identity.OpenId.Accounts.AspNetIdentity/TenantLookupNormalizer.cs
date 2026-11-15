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
/// The normalizer fails closed. It throws outside a tenant scope, an unscoped scope
/// (<see cref="IAmbientTenantAccessor.IsUnscoped"/>) included, so no lookup or write stores or compares a normalized
/// name without its tenant.
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
        if (value is null)
        {
            return null;
        }

        var tenantId =
            AmbientTenantAccessor.TenantId
            ?? throw new InvalidOperationException(
                "A user name or email is unique only within a tenant and is normalized only inside a tenant scope. Open one with IAmbientTenantAccessor.BeginScope."
            );

        var upper = value.Normalize().ToUpperInvariant();
        return $"{tenantId.ToLowerInvariant()}|{upper}";
    }
}
