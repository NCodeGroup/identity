// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Jose.Credentials;
using NCode.Identity.Jwt;
using NCode.Identity.Jwt.Encoding;
using NCode.Identity.Jwt.Validation;
using NCode.Identity.OpenId.Client.Tokens;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using Xunit;

namespace NCode.Identity.OpenId.Client.Tests.Tokens;

public class IssuerAccessTokenValidatorKeysTests : BaseTests, IAsyncLifetime
{
    private const string Issuer = "https://op.example.test";
    private const string Audience = "urn:ncode:management";

    private ServiceProvider ServiceProvider { get; }
    private RSA Rsa { get; } = RSA.Create(2048);
    private SecretKey RsaKey { get; }
    private SecretKey UnknownKey { get; }

    public IssuerAccessTokenValidatorKeysTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSecretsLibrary().AddJoseLibrary().AddJwtLibrary();
        ServiceProvider = serviceCollection.BuildServiceProvider();

        var factory = ServiceProvider.GetRequiredService<ISecretKeyFactory>();
        RsaKey = factory.CreateRsa(new KeyMetadata { KeyId = "k1" }, Rsa);
        UnknownKey = factory.CreateRsa(new KeyMetadata { KeyId = "unknown" }, Rsa);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
        Rsa.Dispose();
    }

    private ISecretKeyCollection CreateKeys(params SecretKey[] keys) =>
        ServiceProvider.GetRequiredService<ISecretKeyCollectionFactory>().Create(keys);

    private DefaultIssuerAccessTokenValidator CreateValidator() =>
        new(
            ServiceProvider.GetRequiredService<IAlgorithmCollectionProvider>(),
            CreateStrictMock<IProviderMetadataResolver>().Object,
            CreateStrictMock<IProviderKeySetResolver>().Object,
            ServiceProvider.GetRequiredService<IJwtValidator>()
        );

    private async ValueTask<string> CreateAccessTokenAsync(
        string tokenAudience,
        SecretKey signingKey,
        CancellationToken cancellationToken
    )
    {
        var issuedAt = DateTimeOffset.UtcNow;
        Assert.True(
            ServiceProvider
                .GetRequiredService<IAlgorithmCollectionProvider>()
                .Collection.TryGetSignatureAlgorithm("PS256", out var algorithm)
        );

        return await ServiceProvider
            .GetRequiredService<IJwtEncoder>()
            .EncodeAsync(
                new JwtEncodingParameters
                {
                    SigningCredentials = new JoseSigningCredentials(signingKey, algorithm),
                    EnsureTokenLifetime = false,
                    TokenType = JoseTokenTypes.AccessTokenJwt,
                    Issuer = Issuer,
                    Audience = new StringValues(tokenAudience),
                    IssuedAt = issuedAt,
                    NotBefore = issuedAt,
                    Expires = issuedAt + TimeSpan.FromMinutes(2),
                    ExtraPayloadClaims = new Dictionary<string, object> { ["sub"] = "user-1" },
                },
                cancellationToken
            );
    }

    private static IssuerAccessTokenValidationRequest CreateRequest(
        string token,
        ISecretKeyCollection keys
    ) =>
        new()
        {
            Issuer = Issuer,
            Audience = Audience,
            Token = token,
            SigningKeys = keys,
        };

    [Fact]
    public async Task ValidateAsync_WhenSignedWithGivenKey_IsValidWithoutNetworkCalls()
    {
        var validator = CreateValidator();
        var token = await CreateAccessTokenAsync(Audience, RsaKey, CancellationToken.None);

        var result = await validator.ValidateAsync(
            CreateRequest(token, CreateKeys(RsaKey)),
            CancellationToken.None
        );

        Assert.True(result.IsValid, result.FailureReason);
        Assert.Equal(Issuer, result.Issuer);
        Assert.Equal("user-1", result.Payload.GetProperty("sub").GetString());
    }

    [Fact]
    public async Task ValidateAsync_WhenKidIsUnknown_FailsWithInvalidSignature()
    {
        var validator = CreateValidator();
        var token = await CreateAccessTokenAsync(Audience, UnknownKey, CancellationToken.None);

        var result = await validator.ValidateAsync(
            CreateRequest(token, CreateKeys(RsaKey)),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.False(result.IsUnavailable);
        Assert.Equal(TokenValidationFailureCodes.InvalidSignature, result.FailureCode);
    }

    [Fact]
    public async Task ValidateAsync_WhenAudienceIsWrong_FailsWithInvalidAudience()
    {
        var validator = CreateValidator();
        var token = await CreateAccessTokenAsync("urn:ncode:other", RsaKey, CancellationToken.None);

        var result = await validator.ValidateAsync(
            CreateRequest(token, CreateKeys(RsaKey)),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.Equal(TokenValidationFailureCodes.InvalidAudience, result.FailureCode);
    }
}
