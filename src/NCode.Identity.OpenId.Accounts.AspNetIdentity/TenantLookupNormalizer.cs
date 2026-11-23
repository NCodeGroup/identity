// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Identity;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

/// <summary>
/// Normalizes the names and emails that ASP.NET Core Identity looks users up by, and prefixes each with the normalized
/// identifier of the ambient tenant and the connection of the ambient connection scope
/// (<c>{normalizedTenantId}|{normalizedConnectionId}|{value}</c>), so a user name or email is unique per connection
/// and a lookup in one connection never finds a user of another.
/// </summary>
/// <remarks>
/// The normalizer fails closed. It throws outside a tenant scope, an unscoped scope
/// (<see cref="IAmbientTenantAccessor.IsUnscoped"/>) included, and outside the connection scope that the store opens
/// around each user manager call, so no lookup or write stores or compares a normalized name without its tenant and
/// connection. The host's Identity schema must size <c>NormalizedUserName</c> to at least
/// <see cref="AccountMaxLengths.NormalizedUserName"/> characters, <c>NormalizedEmail</c> to at least
/// <see cref="AccountMaxLengths.NormalizedEmail"/>, and <c>UserName</c> and <c>Email</c> to at least
/// <see cref="AccountMaxLengths.UserName"/> and <see cref="AccountMaxLengths.Email"/>; the default Entity Framework
/// Core model of ASP.NET Core Identity sizes all four at 256, which is shorter. An Entity Framework Core store sizes
/// the four columns with <c>HasTenantLookupLengths()</c> from
/// <c>NCode.Identity.OpenId.Accounts.AspNetIdentity.EntityFrameworkCore</c>; another store sizes them to
/// <see cref="AccountMaxLengths"/> itself.
/// </remarks>
/// <param name="ambientTenantAccessor">
/// The ambient tenant accessor that supplies the tenant of the current scope.
/// </param>
internal class TenantLookupNormalizer(IAmbientTenantAccessor ambientTenantAccessor)
    : ILookupNormalizer
{
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public string? NormalizeName(string? name) => Normalize(name);

    /// <inheritdoc />
    public string? NormalizeEmail(string? email) => Normalize(email);

    /// <summary>
    /// Normalizes <paramref name="value"/> to its NFC, upper-case invariant form and prefixes it with the ambient
    /// tenant's and the ambient connection's lower-case invariant identifiers, each followed by a <c>|</c> separator.
    /// </summary>
    /// <param name="value">The user name or email to normalize, or <see langword="null"/>.</param>
    /// <returns>
    /// The prefixed normalized value (<c>{normalizedTenantId}|{normalizedConnectionId}|{value}</c>), or
    /// <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="value"/> is not <see langword="null"/> and the ambient tenant accessor has no tenant, which is
    /// the case outside a tenant scope and inside an unscoped scope, or no connection scope is open.
    /// </exception>
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

        var connectionId =
            AmbientLookupConnection.ConnectionId
            ?? throw new InvalidOperationException(
                "A user name or email is unique only within a connection and is normalized only inside a connection scope that the local-account store opens."
            );

        var upper = value.Normalize().ToUpperInvariant();
        return $"{tenantId.ToLowerInvariant()}|{connectionId.ToLowerInvariant()}|{upper}";
    }
}
