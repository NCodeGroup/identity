// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.Jose;
using NCode.Identity.Jwt;
using NCode.Identity.OpenId.Client.Authentication;
using NCode.Identity.OpenId.Client.Authorization;
using NCode.Identity.OpenId.Client.Discovery;
using NCode.Identity.OpenId.Client.Http;
using NCode.Identity.OpenId.Client.Keys;
using NCode.Identity.OpenId.Client.MutualTls;
using NCode.Identity.OpenId.Client.Options;
using NCode.Identity.OpenId.Client.ProofOfPossession;
using NCode.Identity.OpenId.Client.Tokens;
using NCode.Identity.OpenId.Client.UserInfo;
using NCode.Identity.Registration;
using NCode.Identity.Secrets;
using NCode.Identity.SharedState;
using Xunit;

namespace NCode.Identity.OpenId.Client.Tests;

public sealed class DefaultRegistrationTests : IAsyncLifetime
{
    private ServiceCollection Services { get; } = new();

    private IServiceBuilder<JwtLibrary> JwtBuilder { get; }

    private ServiceProvider? Provider { get; set; }

    public DefaultRegistrationTests()
    {
        Services.AddLogging();
        JwtBuilder = Services.AddSecretsLibrary().AddJoseLibrary().AddJwtLibrary();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (Provider is not null)
            await Provider.DisposeAsync();
    }

    private ServiceProvider BuildProvider()
    {
        Provider = Services.BuildServiceProvider();
        return Provider;
    }

    private sealed class CustomClientAuthenticator : IClientAuthenticator
    {
        public string MethodName => "custom";

        public bool UsesMutualTls => false;

        public ValueTask AuthenticateAsync(
            ClientAuthenticationContext context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    #region AddOpenIdClientLibrary Tests

    [Theory]
    [InlineData(typeof(IProviderMetadataResolver), typeof(DefaultProviderMetadataResolver))]
    [InlineData(typeof(IProviderKeySetResolver), typeof(DefaultProviderKeySetResolver))]
    [InlineData(typeof(IClientKeyRingProvider), typeof(DefaultClientKeyRingProvider))]
    [InlineData(typeof(IAuthorizationRequestClient), typeof(DefaultAuthorizationRequestClient))]
    [InlineData(typeof(ITokenClient), typeof(DefaultTokenClient))]
    [InlineData(typeof(IUserInfoClient), typeof(DefaultUserInfoClient))]
    [InlineData(typeof(IDPoPProofFactory), typeof(DefaultDPoPProofFactory))]
    [InlineData(typeof(ITokenRevocationClient), typeof(DefaultTokenRevocationClient))]
    [InlineData(typeof(IIdTokenValidator), typeof(DefaultIdTokenValidator))]
    [InlineData(typeof(ILogoutTokenValidator), typeof(DefaultLogoutTokenValidator))]
    [InlineData(typeof(IIssuerAccessTokenValidator), typeof(DefaultIssuerAccessTokenValidator))]
    public void AddOpenIdClientLibrary_Always_RegistersTheDefaultImplementationAsASingleton(
        Type serviceType,
        Type implementationType
    )
    {
        JwtBuilder.AddOpenIdClientLibrary();
        Services.AddSingleton(new Mock<IExpiringKeySet>(MockBehavior.Strict).Object);

        var descriptor = Assert.Single(Services, x => x.ServiceType == serviceType);
        Assert.Equal(implementationType, descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        var provider = BuildProvider();

        Assert.IsType(implementationType, provider.GetRequiredService(serviceType));
    }

    [Fact]
    public void AddOpenIdClientLibrary_RegistersTheFiveBuiltInAuthenticators()
    {
        JwtBuilder.AddOpenIdClientLibrary();

        var provider = BuildProvider();

        var names = provider
            .GetServices<IClientAuthenticator>()
            .Select(authenticator => authenticator.MethodName)
            .Order(StringComparer.Ordinal)
            .ToList();
        var expected = new[]
        {
            OpenIdProtocol.ClientAuthenticationMethods.ClientSecretBasic,
            OpenIdProtocol.ClientAuthenticationMethods.ClientSecretPost,
            OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt,
            OpenIdProtocol.ClientAuthenticationMethods.TlsClientAuth,
            OpenIdProtocol.ClientAuthenticationMethods.SelfSignedTlsClientAuth,
        }
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expected, names);
    }

    [Fact]
    public void AddOpenIdClientLibrary_WhenCalledTwice_Throws()
    {
        JwtBuilder.AddOpenIdClientLibrary();

        Assert.Throws<InvalidOperationException>(() => JwtBuilder.AddOpenIdClientLibrary());
    }

    [Fact]
    public void AddOpenIdClientLibrary_WithInvalidOptions_ThrowsOnValue()
    {
        JwtBuilder.AddOpenIdClientLibrary();
        Services.Configure<OpenIdClientOptions>(options => options.TotalTimeout = TimeSpan.Zero);

        var provider = BuildProvider();

        var options = provider.GetRequiredService<IOptions<OpenIdClientOptions>>();
        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void AddOpenIdClientLibrary_ConfiguresHandlerWithoutRedirectsOrCookies()
    {
        JwtBuilder.AddOpenIdClientLibrary();

        var provider = BuildProvider();

        var handler = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(OpenIdClientDefaults.HttpClientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        var socketsHandler = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(socketsHandler.AllowAutoRedirect);
        Assert.False(socketsHandler.UseCookies);
    }

    [Fact]
    public void AddOpenIdClientLibrary_BoundsTheNamedClient()
    {
        JwtBuilder.AddOpenIdClientLibrary();

        var provider = BuildProvider();

        var handler = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(OpenIdClientDefaults.HttpClientName);
        var isBounded = false;
        while (handler is DelegatingHandler delegating)
        {
            isBounded |= delegating is ProviderResilienceHandler;
            handler = delegating.InnerHandler!;
        }

        Assert.True(isBounded);
    }

    [Fact]
    public void AddOpenIdClientLibrary_DisablesTheHttpClientTimeout()
    {
        JwtBuilder.AddOpenIdClientLibrary();

        var provider = BuildProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(OpenIdClientDefaults.HttpClientName);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void AddOpenIdClientLibrary_ConfiguresMutualTlsHttpClients()
    {
        JwtBuilder.AddOpenIdClientLibrary();

        var provider = BuildProvider();

        var options = provider
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(OpenIdClientDefaults.MutualTlsHttpClientNamePrefix + "ABC");
        Assert.NotEmpty(options.HttpMessageHandlerBuilderActions);
        Assert.Same(
            provider.GetRequiredService<ClientCertificateProvider>(),
            provider.GetRequiredService<ClientCertificateProvider>()
        );
    }

    #endregion

    #region AddClientAuthenticator Tests

    [Fact]
    public void AddClientAuthenticator_AddsTheAuthenticator()
    {
        JwtBuilder.AddOpenIdClientLibrary().AddClientAuthenticator<CustomClientAuthenticator>();

        var provider = BuildProvider();

        var names = provider.GetServices<IClientAuthenticator>().Select(x => x.MethodName);
        Assert.Contains("custom", names);
    }

    #endregion
}
