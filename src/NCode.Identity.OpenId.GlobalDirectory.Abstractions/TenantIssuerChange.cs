// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the change of a tenant's issuer that <see cref="ITenantDirectory.ChangeIssuerAsync"/> checks and records.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record TenantIssuerChange
{
    /// <summary>
    /// Gets the identifier of the tenant.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the issuer the tenant gives up, or <c>null</c> when the tenant had none.
    /// </summary>
    public string? PreviousIssuer { get; init; }

    /// <summary>
    /// Gets the issuer the tenant takes, or <c>null</c> when the tenant takes none.
    /// </summary>
    public string? Issuer { get; init; }
}
