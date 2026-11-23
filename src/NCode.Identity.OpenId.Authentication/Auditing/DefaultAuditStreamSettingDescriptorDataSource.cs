// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using NCode.Collections.Providers;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Authentication.Auditing;

/// <summary>
/// Provides an additive <see cref="SettingDescriptor"/> data source for the audit-stream setting, composed with the
/// base descriptor catalog from the core slice. The setting is the most audit streams a tenant may have, between 0 and
/// 10, and defaults to 2. It is a limit and merges as a ceiling: the administrators above the tenant set it, and a
/// narrower scope may lower its parent's value but never raise it.
/// </summary>
// See ADR-1534.
internal class DefaultAuditStreamSettingDescriptorDataSource(INullChangeToken nullChangeToken)
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
            // max_audit_streams: the most audit streams a tenant may have; a tier writes it on a team, and the team's
            // administrators may set lower ones (ADR-1534, ADR-1418).
            yield return new SettingDescriptor<int>
            {
                Name = OpenIdSettingNames.MaxAuditStreams,
                AppliesTo = Tenants,
                Rule = SettingRules.Between(0, 10),
                Default = 2,
                IsLimit = true,
                IsDiscoverable = false,
                OnMerge = SettingMerge.Min,
            };
        }
    }
}
