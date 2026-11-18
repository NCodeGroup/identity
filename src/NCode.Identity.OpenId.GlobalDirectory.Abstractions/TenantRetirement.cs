// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the identifier and issuer that <see cref="ITenantDirectory.RetireAsync"/> retires for a deleted tenant.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record TenantRetirement
{
    /// <summary>
    /// Gets the identifier of the deleted tenant.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the issuer of the deleted tenant, or <c>null</c> when the tenant had none.
    /// </summary>
    public string? Issuer { get; init; }
}
