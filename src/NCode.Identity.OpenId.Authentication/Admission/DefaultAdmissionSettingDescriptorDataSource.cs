// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using NCode.Collections.Providers;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Authentication.Admission;

/// <summary>
/// Provides an additive <see cref="SettingDescriptor"/> data source for the admission setting, composed with the base
/// descriptor catalog from the core slice. The setting is a percentage of a replica's request capacity, has no default
/// (unset means the whole capacity), and merges as a ceiling: a narrower scope may lower its parent's share but never
/// raise it. It is a limit: the administrators above the scope whose document carries it set it, so a tenant never
/// raises its own.
/// </summary>
// See ADR-1523.
// See ADR-1418.
internal class DefaultAdmissionSettingDescriptorDataSource(INullChangeToken nullChangeToken)
    : ICollectionDataSource<SettingDescriptor>
{
    private static ImmutableArray<string> Tenants { get; } = [OpenIdSettingScopes.Tenant];

    private INullChangeToken NullChangeToken { get; } = nullChangeToken;

    /// <inheritdoc />
    public IChangeToken GetChangeToken() => NullChangeToken;

    /// <inheritdoc />
    public IEnumerable<SettingDescriptor> Collection
    {
        get
        {
            // admission_capacity_percent: the share of a replica's request capacity the tenant's protocol requests may
            // fill; the platform writes a tier's share on a team, and the team's administrators may set lower ones on
            // its tenants (ADR-1523, ADR-1418).
            yield return new SettingDescriptor<int>
            {
                Name = OpenIdSettingNames.AdmissionCapacityPercent,
                AppliesTo = Tenants,
                Rule = SettingRules.Between(
                    AdmissionCapacity.MinPercent,
                    AdmissionCapacity.FullPercent
                ),
                IsLimit = true,
                OnMerge = SettingMerge.Min,
            };
        }
    }
}
