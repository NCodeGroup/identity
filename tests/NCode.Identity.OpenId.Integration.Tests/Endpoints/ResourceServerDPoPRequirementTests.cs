// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NCode.Buffers;
using NCode.Identity.Jose;
using NCode.Identity.Jwt;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.Integration.Tests.Infrastructure;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Endpoints;

/// <summary>
/// Verifies that a resource server's <c>require_dpop_bound_access_tokens</c> setting refuses a token request that
/// targets it without a DPoP proof, and issues a DPoP-bound token for a request that carries one.
/// </summary>
public class ResourceServerDPoPRequirementTests
{
    private const string TokenPath = "/oauth2/token";
    private const string SecretKeyRing = "rs-dpop-secret";
    private const string DPoPKeyRing = "rs-dpop-key";
    private const string ClientSecret = "rs-dpop-Wq4mZ8vK2nT7xR5bL9cY3hF6jD1sGp0E";

    private static async ValueTask<HostApplicationFactory> SeedAsync(
        string clientId,
        CancellationToken cancellationToken
    )
    {
        var factory = new HostApplicationFactory();
        await factory.SeedSharedSecretClientWithSettingsAsync(
            clientId,
            ClientSecret,
            OpenIdProtocol.ClientAuthenticationMethods.ClientSecretBasic,
            new Dictionary<string, object>(),
            [],
            cancellationToken
        );
        await factory.SeedResourceServerWithClientGrantAsync(
            clientId,
            $"{clientId}-rs-api",
            $"https://api.{clientId}.integration.test",
            new Dictionary<string, object>
            {
                [OpenIdSettingNames.RequireDPoPBoundAccessTokens] = true,
            },
            [TestPlatformSettingsProvider.ApiScope],
            cancellationToken
        );
        return factory;
    }

    private static void AddRing(
        ServiceCollection serviceCollection,
        string name,
        Func<ISecretKeyFactory, SecretKey[]> createKeys
    ) =>
        serviceCollection.AddKeyedSingleton<ISecretKeyCollectionProvider>(
            name,
            (serviceProvider, _) =>
                serviceProvider
                    .GetRequiredService<ISecretKeyCollectionProviderFactory>()
                    .CreateStatic(
                        createKeys(serviceProvider.GetRequiredService<ISecretKeyFactory>())
                    )
        );

    private static ServiceProvider CreateClientProvider(HostApplicationFactory factory, ECDsa key)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection
            .AddSecretsLibrary()
            .AddJoseLibrary()
            .AddJwtLibrary()
            .AddOpenIdClientLibrary();
        serviceCollection
            .AddHttpClient(OpenIdClientDefaults.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler());

        AddRing(
            serviceCollection,
            SecretKeyRing,
            keyFactory =>
                [keyFactory.CreateSymmetric(new KeyMetadata { KeyId = "s" }, ClientSecret.AsSpan())]
        );
        AddRing(
            serviceCollection,
            DPoPKeyRing,
            keyFactory =>
                [keyFactory.CreateEcc(new KeyMetadata { KeyId = $"{DPoPKeyRing}-a" }, key)]
        );
        return serviceCollection.BuildServiceProvider();
    }

    [Fact]
    public async Task PostToken_WhenTheResourceServerRequiresDPoPAndTheRequestHasNoProof_IsRefused()
    {
        const string clientId = "it-rs-dpop-no-proof";
        using var factory = await SeedAsync(clientId, CancellationToken.None);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = OpenIdProtocol.GrantTypes.ClientCredentials,
                ["scope"] = TestPlatformSettingsProvider.ApiScope,
            }
        );
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(SecureEncoding.UTF8.GetBytes($"{clientId}:{ClientSecret}"))
        );

        using var response = await client.PostAsync(TokenPath, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(
            OpenIdProtocol.ErrorCodes.InvalidDPoPProof,
            JsonElement.Parse(body).GetProperty("error").GetString()
        );
    }

    [Fact]
    public async Task PostToken_WhenTheResourceServerRequiresDPoPAndTheRequestHasAProof_IssuesABoundToken()
    {
        const string clientId = "it-rs-dpop-proof";
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var factory = await SeedAsync(clientId, CancellationToken.None);
        await using var provider = CreateClientProvider(factory, key);
        var registration = new ClientRegistration(
            HostApplicationFactory.TestServerIssuer,
            clientId,
            OpenIdProtocol.ClientAuthenticationMethods.ClientSecretBasic
        )
        {
            KeyRing = SecretKeyRing,
            DPoPKeyRing = DPoPKeyRing,
        };

        var result = await provider
            .GetRequiredService<ITokenClient>()
            .RequestTokenAsync(
                registration,
                new ClientCredentialsTokenRequest { Scope = TestPlatformSettingsProvider.ApiScope },
                CancellationToken.None
            );

        Assert.True(result.IsSuccess, $"{result.Error?.Error}: {result.Error?.ErrorDescription}");
        Assert.Equal(OpenIdProtocol.TokenTypes.DPoP, result.Response.TokenType, ignoreCase: true);
    }
}
