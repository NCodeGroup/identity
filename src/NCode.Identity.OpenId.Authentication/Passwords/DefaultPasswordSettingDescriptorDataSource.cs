// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using NCode.Collections.Providers;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Authentication.Passwords;

/// <summary>
/// Provides an additive <see cref="SettingDescriptor"/> data source for the password settings, composed with the base
/// descriptor catalog from the core slice: the length a local account's password must have, and the failed attempts an
/// account may accumulate before its passwords are refused. Every setting applies to tenants and to their connections
/// and is not advertised.
/// </summary>
// See ADR-1236 and ADR-1237.
internal class DefaultPasswordSettingDescriptorDataSource(INullChangeToken nullChangeToken)
    : ICollectionDataSource<SettingDescriptor>
{
    private static ImmutableArray<string> TenantsAndConnections { get; } =
    [OpenIdSettingScopes.Tenant, OpenIdSettingScopes.Connection];

    private INullChangeToken NullChangeToken { get; } = nullChangeToken;

    /// <inheritdoc />
    public IChangeToken GetChangeToken() => NullChangeToken;

    /// <inheritdoc />
    public IEnumerable<SettingDescriptor> Collection
    {
        get
        {
            // password_min_length: the fewest characters, counted as Unicode code points, a password may have. Between
            // 8 (NIST SP 800-63B's absolute floor) and 64; default 15 (NIST's requirement for a password that is the
            // only authentication factor). Floor (Max): a narrower scope may only raise it.
            yield return new SettingDescriptor<int>
            {
                Name = OpenIdSettingNames.PasswordMinLength,
                AppliesTo = TenantsAndConnections,
                Rule = SettingRules.Between(8, 64),
                Default = 15,
                IsDiscoverable = false,
                OnMerge = SettingMerge.Max,
            };

            // password_max_length: the most characters, counted as Unicode code points, a password may have. Between
            // 64 (NIST's minimum for the maximum) and 4096; default 128. Override (Replace): a narrower scope fully
            // replaces the value.
            yield return new SettingDescriptor<int>
            {
                Name = OpenIdSettingNames.PasswordMaxLength,
                AppliesTo = TenantsAndConnections,
                Rule = SettingRules.Between(64, 4096),
                Default = 128,
                IsDiscoverable = false,
                OnMerge = SettingMerge.Replace,
            };

            // password_failed_attempts_limit: the failed password attempts an account may accumulate within the window
            // before every password for it is refused. Between 1 and 100 (NIST's maximum); default 10. Ceiling (Min):
            // a narrower scope may only lower it.
            yield return new SettingDescriptor<int>
            {
                Name = OpenIdSettingNames.PasswordFailedAttemptsLimit,
                AppliesTo = TenantsAndConnections,
                Rule = SettingRules.Between(1, 100),
                Default = 10,
                IsDiscoverable = false,
                OnMerge = SettingMerge.Min,
            };

            // password_failed_attempts_window: how long the failed attempts, counted from the first one, are kept.
            // Between one minute and one day; default 15 minutes. Floor (Max): a narrower scope may only lengthen it.
            var windowMinimum = TimeSpan.FromMinutes(1);
            var windowMaximum = TimeSpan.FromDays(1);
            yield return new SettingDescriptor<TimeSpan>
            {
                Name = OpenIdSettingNames.PasswordFailedAttemptsWindow,
                AppliesTo = TenantsAndConnections,
                Rule = SettingRules.Between(windowMinimum, windowMaximum),
                Default = TimeSpan.FromMinutes(15),
                IsDiscoverable = false,
                OnMerge = SettingMerge.Max,
            };
        }
    }
}
