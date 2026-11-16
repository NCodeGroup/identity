// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.Tenants;

/// <summary>
/// Represents the request to preview or apply the promotion of a source configuration into the tenant that the route
/// names. The request names its source in exactly one of two ways: <see cref="SourceTenantId"/>, a tenant of the
/// target's team in the same cell, or <see cref="Source"/>, a configuration document exported by a cell of the same
/// region.
/// </summary>
// See ADR-1425.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PromoteTenantRequest
{
    /// <summary>
    /// Gets the identifier of the tenant whose configuration is promoted. It differs from the target tenant. The
    /// request gives either this or <see cref="Source"/>, not both.
    /// </summary>
    public string? SourceTenantId { get; init; }

    /// <summary>
    /// Gets the configuration document that is promoted. The request gives either this or <see cref="SourceTenantId"/>,
    /// not both.
    /// </summary>
    public TenantConfigurationDocument? Source { get; init; }

    /// <summary>
    /// Gets the environment-bound setting values the target keeps for the resources the promotion writes. A resource
    /// the promotion writes holds the source's other settings, then the target's own environment-bound values, then the
    /// values given here.
    /// </summary>
    public List<TenantPromotionEnvironmentSettings> EnvironmentSettings { get; init; } = [];
}
