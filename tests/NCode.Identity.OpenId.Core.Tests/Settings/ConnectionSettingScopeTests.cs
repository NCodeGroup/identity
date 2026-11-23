// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Collections.Providers;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Locales;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Secrets;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Settings;

public class ConnectionSettingScopeTests : BaseTests
{
    private static string[] ConnectionScopedSettingNames { get; } =
    [
        OpenIdSettingNames.FederatedIdentityJoinClaim,
        OpenIdSettingNames.FederatedIdentityVerifiedClaim,
        OpenIdSettingNames.FederatedIdentityRequireVerified,
        OpenIdSettingNames.FederatedIdentityExplicitOnly,
        OpenIdSettingNames.PasswordMinLength,
        OpenIdSettingNames.PasswordMaxLength,
        OpenIdSettingNames.PasswordFailedAttemptsLimit,
        OpenIdSettingNames.PasswordFailedAttemptsWindow,
        OpenIdSettingNames.PasswordResetEnabled,
        OpenIdSettingNames.EmailVerificationEnabled,
        OpenIdSettingNames.EmailMfaRequired,
        OpenIdSettingNames.LocalAccountIdentifier,
    ];

    private Dictionary<string, SettingDescriptor> CreateDescriptors()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSecretsLibrary().AddJoseLibrary();
        var algorithms = services
            .BuildServiceProvider()
            .GetRequiredService<IAlgorithmCollectionProvider>();

        var nullChangeToken = CreateStrictMock<INullChangeToken>().Object;
        var core = new DefaultSettingDescriptorDataSource(
            nullChangeToken,
            algorithms,
            [],
            [],
            new DefaultUiLocaleCatalog([])
        );
        var password = new DefaultPasswordSettingDescriptorDataSource(nullChangeToken);
        var emailCode = new DefaultEmailCodeSettingDescriptorDataSource(nullChangeToken);

        return core
            .Collection.Concat(password.Collection)
            .Concat(emailCode.Collection)
            .ToDictionary(descriptor => descriptor.Name);
    }

    [Fact]
    public void Collection_WhenEnumerated_ContainsConnectionBeneathTenant()
    {
        var dataSource = new DefaultSettingScopeDataSource(
            CreateStrictMock<INullChangeToken>().Object
        );

        var scope = Assert.Single(
            dataSource.Collection,
            descriptor => descriptor.Name == OpenIdSettingScopes.Connection
        );

        Assert.Equal(OpenIdSettingScopes.Tenant, scope.ParentName);
    }

    [Fact]
    public void Collection_WhenEnumerated_ListedDescriptorsApplyToConnection()
    {
        var descriptors = CreateDescriptors();

        foreach (var name in ConnectionScopedSettingNames)
        {
            Assert.Contains(OpenIdSettingScopes.Connection, descriptors[name].AppliesTo);
        }
    }

    [Theory]
    [InlineData(OpenIdSettingNames.EmailCodeLifetime)]
    [InlineData(OpenIdSettingNames.EmailCodeSendsPerHour)]
    public void Collection_WhenMailDeliveryLimit_AppliesOnlyToTenants(string name)
    {
        var descriptors = CreateDescriptors();

        Assert.DoesNotContain(OpenIdSettingScopes.Connection, descriptors[name].AppliesTo);
    }

    [Fact]
    public void Collection_WhenAllowedConnections_AppliesToClientsWithoutDefault()
    {
        var descriptor = CreateDescriptors()[OpenIdSettingNames.AllowedConnections];

        Assert.Equal([OpenIdSettingScopes.Client], descriptor.AppliesTo.ToArray());
        Assert.False(descriptor.HasDefault);
        Assert.True(
            descriptor.TryValidate((IReadOnlyCollection<string>)["local", "corp-1"], out _)
        );
        Assert.False(descriptor.TryValidate((IReadOnlyCollection<string>)["-bad"], out _));
    }

    [Fact]
    public void Collection_WhenDefaultConnection_DefaultsToLocal()
    {
        var descriptor =
            (SettingDescriptor<string>)CreateDescriptors()[OpenIdSettingNames.DefaultConnection];

        Assert.Equal(ConnectionDefaults.Name, descriptor.Default);
        Assert.Equal(
            [OpenIdSettingScopes.Tenant, OpenIdSettingScopes.Client],
            descriptor.AppliesTo.ToArray()
        );
        Assert.False(descriptor.TryValidate("not valid", out _));
    }

    [Fact]
    public void Collection_WhenLocalAccountIdentifier_DefaultsToEmailOrUserName()
    {
        var descriptor =
            (SettingDescriptor<string>)
                CreateDescriptors()[OpenIdSettingNames.LocalAccountIdentifier];

        Assert.Equal(LocalAccountIdentifiers.EmailOrUserName, descriptor.Default);
        Assert.True(descriptor.TryValidate(LocalAccountIdentifiers.Email, out _));
        Assert.True(descriptor.TryValidate(LocalAccountIdentifiers.UserName, out _));
        Assert.False(descriptor.TryValidate("phone", out _));
    }
}
