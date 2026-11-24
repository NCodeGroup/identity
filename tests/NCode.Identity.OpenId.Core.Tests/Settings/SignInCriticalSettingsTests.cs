// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Collections.Providers;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.OpenId.Authentication.Passwords;
using NCode.Identity.OpenId.Locales;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Secrets;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Settings;

public class SignInCriticalSettingsTests : BaseTests
{
    private static string[] ExpectedNames { get; } =
    [
        OpenIdSettingNames.AllowedConnections,
        OpenIdSettingNames.DefaultConnection,
        OpenIdSettingNames.GrantTypesEnabled,
        OpenIdSettingNames.PasswordMinLength,
        OpenIdSettingNames.PasswordMaxLength,
        OpenIdSettingNames.PasswordFailedAttemptsLimit,
        OpenIdSettingNames.PasswordFailedAttemptsWindow,
        OpenIdSettingNames.EmailMfaRequired,
        OpenIdSettingNames.RequireDPoPBoundAccessTokens,
        OpenIdSettingNames.AccessTokenSenderConstraintRequired,
    ];

    [Fact]
    public void Catalog_SignInCriticalSettings_AreTheListedNames()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSecretsLibrary().AddJoseLibrary();
        var algorithms = serviceCollection
            .BuildServiceProvider()
            .GetRequiredService<IAlgorithmCollectionProvider>();
        var nullChangeToken = CreateStrictMock<INullChangeToken>().Object;

        var descriptors = new List<SettingDescriptor>();
        descriptors.AddRange(
            new DefaultSettingDescriptorDataSource(
                nullChangeToken,
                algorithms,
                [],
                [
                    new HttpsRedirectUriKind(),
                    new LoopbackRedirectUriKind(),
                    new HttpRedirectUriKind(),
                    new PrivateUseRedirectUriKind(),
                ],
                new DefaultUiLocaleCatalog([])
            ).Collection
        );
        descriptors.AddRange(
            new DefaultPasswordSettingDescriptorDataSource(nullChangeToken).Collection
        );
        descriptors.AddRange(
            new DefaultEmailCodeSettingDescriptorDataSource(nullChangeToken).Collection
        );

        var actual = descriptors
            .Where(descriptor => descriptor.IsSignInCritical)
            .Select(descriptor => descriptor.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ExpectedNames.Order(StringComparer.Ordinal), actual);
    }
}
