// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Collections.Providers;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Passwords;

public sealed class DefaultEmailCodeSettingDescriptorDataSourceTests : BaseTests
{
    private DefaultEmailCodeSettingDescriptorDataSource CreateDataSource() =>
        new(CreateStrictMock<INullChangeToken>().Object);

    private static SettingDescriptor<T> GetDescriptor<T>(
        DefaultEmailCodeSettingDescriptorDataSource dataSource,
        string name
    )
        where T : notnull =>
        Assert.IsType<SettingDescriptor<T>>(
            Assert.Single(dataSource.Collection, descriptor => descriptor.Name == name)
        );

    #region Collection Property Tests

    [Fact]
    public void Collection_Always_DescribesTheFiveEmailCodeSettings()
    {
        var dataSource = CreateDataSource();

        var descriptors = dataSource.Collection.ToList();

        Assert.Equal(5, descriptors.Count);
        Assert.Equal(
            [
                OpenIdSettingNames.EmailCodeLifetime,
                OpenIdSettingNames.EmailCodeSendsPerHour,
                OpenIdSettingNames.EmailMfaRequired,
                OpenIdSettingNames.EmailVerificationEnabled,
                OpenIdSettingNames.PasswordResetEnabled,
            ],
            descriptors.Select(descriptor => descriptor.Name).Order(StringComparer.Ordinal)
        );
        foreach (var descriptor in descriptors)
        {
            var expectedScopes = descriptor.Name
                is OpenIdSettingNames.EmailCodeLifetime
                    or OpenIdSettingNames.EmailCodeSendsPerHour
                ? new[] { OpenIdSettingScopes.Tenant }
                : [OpenIdSettingScopes.Tenant, OpenIdSettingScopes.Connection];
            Assert.Equal<string>(expectedScopes, descriptor.AppliesTo);
            Assert.False(descriptor.IsDiscoverable);
        }

        var lifetime = GetDescriptor<TimeSpan>(dataSource, OpenIdSettingNames.EmailCodeLifetime);
        Assert.Equal(TimeSpan.FromMinutes(10), lifetime.Default);
        Assert.False(lifetime.TryValidate(TimeSpan.FromSeconds(59), out _));
        Assert.True(lifetime.TryValidate(TimeSpan.FromMinutes(1), out _));
        Assert.True(lifetime.TryValidate(TimeSpan.FromHours(1), out _));
        Assert.False(lifetime.TryValidate(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1), out _));
        Assert.Equal(
            TimeSpan.FromMinutes(5),
            lifetime.OnMerge(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5))
        );
        Assert.Equal(
            TimeSpan.FromMinutes(10),
            lifetime.OnMerge(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30))
        );

        var sends = GetDescriptor<int>(dataSource, OpenIdSettingNames.EmailCodeSendsPerHour);
        Assert.Equal(5, sends.Default);
        Assert.False(sends.TryValidate(0, out _));
        Assert.True(sends.TryValidate(1, out _));
        Assert.True(sends.TryValidate(20, out _));
        Assert.False(sends.TryValidate(21, out _));
        Assert.Equal(2, sends.OnMerge(5, 2));
        Assert.Equal(5, sends.OnMerge(5, 10));
    }

    [Fact]
    public void Collection_Always_DefaultsEveryFlowToOff()
    {
        var dataSource = CreateDataSource();

        var passwordReset = GetDescriptor<bool>(
            dataSource,
            OpenIdSettingNames.PasswordResetEnabled
        );
        var emailVerification = GetDescriptor<bool>(
            dataSource,
            OpenIdSettingNames.EmailVerificationEnabled
        );
        var emailMfa = GetDescriptor<bool>(dataSource, OpenIdSettingNames.EmailMfaRequired);

        Assert.False(passwordReset.Default);
        Assert.False(emailVerification.Default);
        Assert.False(emailMfa.Default);
        Assert.False(passwordReset.OnMerge(true, false));
        Assert.True(emailVerification.OnMerge(false, true));
    }

    [Fact]
    public void Collection_Always_MergesTheSecondFactorRequirementAsAFloor()
    {
        var dataSource = CreateDataSource();
        var descriptor = GetDescriptor<bool>(dataSource, OpenIdSettingNames.EmailMfaRequired);

        var required = descriptor.OnMerge(false, true);
        var notLifted = descriptor.OnMerge(true, false);

        Assert.True(required);
        Assert.True(notLifted);
    }

    #endregion
}
