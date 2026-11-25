// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using NCode.Collections.Providers;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Authentication.Passwords;

/// <summary>
/// Provides an additive <see cref="SettingDescriptor"/> data source for the email-code settings, composed with the base
/// descriptor catalog from the core slice: how long a mailed one-time code is accepted, how many may be mailed per
/// hour, and whether a tenant's accounts reset a password, verify an address or complete a sign-in with one. The code
/// lifetime and the sends per hour apply to tenants; the other settings apply to tenants and to their connections. No
/// setting is advertised.
/// </summary>
// See ADR-1533.
internal class DefaultEmailCodeSettingDescriptorDataSource(INullChangeToken nullChangeToken)
    : ICollectionDataSource<SettingDescriptor>
{
    private static ImmutableArray<string> Tenants { get; } = [OpenIdSettingScopes.Tenant];

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
            // email_code_lifetime: how long a one-time code mailed to an email address is accepted. Between one minute
            // and one hour; default 10 minutes. Ceiling (Min): a narrower scope may only shorten it.
            var lifetimeMinimum = TimeSpan.FromMinutes(1);
            var lifetimeMaximum = TimeSpan.FromHours(1);
            yield return new SettingDescriptor<TimeSpan>
            {
                Name = OpenIdSettingNames.EmailCodeLifetime,
                AppliesTo = Tenants,
                Rule = SettingRules.Between(lifetimeMinimum, lifetimeMaximum),
                Default = TimeSpan.FromMinutes(10),
                IsDiscoverable = false,
                OnMerge = SettingMerge.Min,
            };

            // email_code_sends_per_hour: the most one-time codes that may be mailed in one hour for one account and
            // purpose. Between 1 and 20; default 5. Ceiling (Min): a narrower scope may only lower it.
            yield return new SettingDescriptor<int>
            {
                Name = OpenIdSettingNames.EmailCodeSendsPerHour,
                AppliesTo = Tenants,
                Rule = SettingRules.Between(1, 20),
                Default = 5,
                IsDiscoverable = false,
                OnMerge = SettingMerge.Min,
            };

            // password_reset_enabled: whether a local account's password may be reset with a mailed one-time code.
            // Default false. Override (Replace): a narrower scope fully replaces the value.
            yield return new SettingDescriptor<bool>
            {
                Name = OpenIdSettingNames.PasswordResetEnabled,
                AppliesTo = TenantsAndConnections,
                Default = false,
                IsDiscoverable = false,
                OnMerge = SettingMerge.Replace,
            };

            // email_verification_enabled: whether a local account may verify its email address with a mailed one-time
            // code. Default false. Override (Replace): a narrower scope fully replaces the value.
            yield return new SettingDescriptor<bool>
            {
                Name = OpenIdSettingNames.EmailVerificationEnabled,
                AppliesTo = TenantsAndConnections,
                Default = false,
                IsDiscoverable = false,
                OnMerge = SettingMerge.Replace,
            };

            // email_mfa_required: whether a local account's sign-in must be completed with a mailed one-time code.
            // Default false. Or: a narrower scope may require it, never lift it.
            yield return new SettingDescriptor<bool>
            {
                Name = OpenIdSettingNames.EmailMfaRequired,
                IsSignInCritical = true,
                AppliesTo = TenantsAndConnections,
                Default = false,
                IsDiscoverable = false,
                OnMerge = SettingMerge.Or,
            };
        }
    }
}
