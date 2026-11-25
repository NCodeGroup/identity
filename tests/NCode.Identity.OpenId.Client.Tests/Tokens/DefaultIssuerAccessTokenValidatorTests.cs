// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Moq;
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

public class DefaultIssuerAccessTokenValidatorTests : BaseTests, IAsyncLifetime
{
    private const string Issuer = "https://op.example.test";
    private const string Audience = "urn:ncode:directory";

    private ServiceProvider ServiceProvider { get; }
    private RSA Rsa { get; } = RSA.Create(2048);
    private RSA OtherRsa { get; } = RSA.Create(2048);
    private SecretKey RsaKey { get; }
    private SecretKey OtherKey { get; }

    public DefaultIssuerAccessTokenValidatorTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSecretsLibrary().AddJoseLibrary().AddJwtLibrary();
        ServiceProvider = serviceCollection.BuildServiceProvider();

        var factory = ServiceProvider.GetRequiredService<ISecretKeyFactory>();
        RsaKey = factory.CreateRsa(new KeyMetadata { KeyId = "k1" }, Rsa);
        OtherKey = factory.CreateRsa(new KeyMetadata { KeyId = "k1" }, OtherRsa);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
        Rsa.Dispose();
        OtherRsa.Dispose();
    }

    private ISecretKeyCollection CreateKeys(params SecretKey[] keys) =>
        ServiceProvider.GetRequiredService<ISecretKeyCollectionFactory>().Create(keys);

    private Mock<IProviderKeySetResolver> CreateKeySetMock()
    {
        var mock = CreateStrictMock<IProviderKeySetResolver>();
        mock.Setup(x => x.GetSigningKeysAsync(Issuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateKeys(RsaKey))
            .Verifiable();
        return mock;
    }

    private Mock<IProviderMetadataResolver> CreateMetadataMock(string? metadataIssuer = Issuer)
    {
        var mock = CreateStrictMock<IProviderMetadataResolver>();
        mock.Setup(x => x.GetMetadataAsync(Issuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenIdProviderMetadata { Issuer = metadataIssuer })
            .Verifiable();
        return mock;
    }

    private DefaultIssuerAccessTokenValidator CreateValidator(
        Mock<IProviderMetadataResolver> metadataMock,
        Mock<IProviderKeySetResolver>? keySetMock = null
    ) =>
        new(
            ServiceProvider.GetRequiredService<IAlgorithmCollectionProvider>(),
            metadataMock.Object,
            (keySetMock ?? CreateStrictMock<IProviderKeySetResolver>()).Object,
            ServiceProvider.GetRequiredService<IJwtValidator>()
        );

    private async ValueTask<string> CreateAccessTokenAsync(
        string tokenIssuer,
        string tokenAudience,
        string tokenType,
        TimeSpan issuedOffset,
        SecretKey signingKey,
        CancellationToken cancellationToken
    )
    {
        var issuedAt = DateTimeOffset.UtcNow + issuedOffset;

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
                    TokenType = tokenType,
                    Issuer = tokenIssuer,
                    Audience = new StringValues(tokenAudience),
                    IssuedAt = issuedAt,
                    NotBefore = issuedAt,
                    Expires = issuedAt + TimeSpan.FromMinutes(2),
                    ExtraPayloadClaims = new Dictionary<string, object>
                    {
                        ["sub"] = "client-1",
                        ["scope"] = "directory",
                    },
                },
                cancellationToken
            );
    }

    private ValueTask<string> CreateValidAccessTokenAsync(CancellationToken cancellationToken) =>
        CreateAccessTokenAsync(
            Issuer,
            Audience,
            JoseTokenTypes.AccessTokenJwt,
            TimeSpan.Zero,
            RsaKey,
            cancellationToken
        );

    private static IssuerAccessTokenValidationRequest CreateRequest(
        string issuer,
        string audience,
        string token
    ) =>
        new()
        {
            Issuer = issuer,
            Audience = audience,
            Token = token,
        };

    #region ValidateAsync Tests

    [Fact]
    public async Task ValidateAsync_WhenValid_ReturnsPayloadAndIssuer()
    {
        var validator = CreateValidator(CreateMetadataMock(), CreateKeySetMock());
        var token = await CreateValidAccessTokenAsync(CancellationToken.None);

        var result = await validator.ValidateAsync(
            CreateRequest(Issuer, Audience, token),
            CancellationToken.None
        );

        Assert.True(result.IsValid, result.FailureReason);
        Assert.False(result.IsUnavailable);
        Assert.Null(result.FailureCode);
        Assert.Equal(Issuer, result.Issuer);
        Assert.Equal("client-1", result.Payload.GetProperty("sub").GetString());
        Assert.Equal("directory", result.Payload.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task ValidateAsync_WhenAudienceIsWrong_FailsWithInvalidAudience()
    {
        var validator = CreateValidator(CreateMetadataMock(), CreateKeySetMock());
        var token = await CreateAccessTokenAsync(
            Issuer,
            "urn:ncode:other",
            JoseTokenTypes.AccessTokenJwt,
            TimeSpan.Zero,
            RsaKey,
            CancellationToken.None
        );

        var result = await validator.ValidateAsync(
            CreateRequest(Issuer, Audience, token),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.False(result.IsUnavailable);
        Assert.Equal(TokenValidationFailureCodes.InvalidAudience, result.FailureCode);
    }

    [Fact]
    public async Task ValidateAsync_WhenIssuerClaimIsWrong_FailsWithInvalidIssuer()
    {
        var validator = CreateValidator(CreateMetadataMock(), CreateKeySetMock());
        var token = await CreateAccessTokenAsync(
            "https://evil.example.test",
            Audience,
            JoseTokenTypes.AccessTokenJwt,
            TimeSpan.Zero,
            RsaKey,
            CancellationToken.None
        );

        var result = await validator.ValidateAsync(
            CreateRequest(Issuer, Audience, token),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.Equal(TokenValidationFailureCodes.InvalidIssuer, result.FailureCode);
    }

    [Fact]
    public async Task ValidateAsync_WhenDiscoveryIssuerDiffers_FailsWithInvalidIssuer()
    {
        var validator = CreateValidator(CreateMetadataMock("https://evil.example.test"));
        var token = await CreateValidAccessTokenAsync(CancellationToken.None);

        var result = await validator.ValidateAsync(
            CreateRequest(Issuer, Audience, token),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.False(result.IsUnavailable);
        Assert.Equal(TokenValidationFailureCodes.InvalidIssuer, result.FailureCode);
    }

    [Fact]
    public async Task ValidateAsync_WhenExpired_FailsWithExpired()
    {
        var validator = CreateValidator(CreateMetadataMock(), CreateKeySetMock());
        var token = await CreateAccessTokenAsync(
            Issuer,
            Audience,
            JoseTokenTypes.AccessTokenJwt,
            TimeSpan.FromMinutes(-20),
            RsaKey,
            CancellationToken.None
        );

        var result = await validator.ValidateAsync(
            CreateRequest(Issuer, Audience, token),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.Equal(TokenValidationFailureCodes.Expired, result.FailureCode);
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenTypeIsWrong_FailsWithInvalidTokenType()
    {
        var validator = CreateValidator(CreateMetadataMock(), CreateKeySetMock());
        var token = await CreateAccessTokenAsync(
            Issuer,
            Audience,
            JoseTokenTypes.Jwt,
            TimeSpan.Zero,
            RsaKey,
            CancellationToken.None
        );

        var result = await validator.ValidateAsync(
            CreateRequest(Issuer, Audience, token),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.Equal(TokenValidationFailureCodes.InvalidTokenType, result.FailureCode);
    }

    [Fact]
    public async Task ValidateAsync_WhenSignedWithAnotherKey_FailsWithInvalidSignature()
    {
        var validator = CreateValidator(CreateMetadataMock(), CreateKeySetMock());
        var token = await CreateAccessTokenAsync(
            Issuer,
            Audience,
            JoseTokenTypes.AccessTokenJwt,
            TimeSpan.Zero,
            OtherKey,
            CancellationToken.None
        );

        var result = await validator.ValidateAsync(
            CreateRequest(Issuer, Audience, token),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.False(result.IsUnavailable);
        Assert.Equal(TokenValidationFailureCodes.InvalidSignature, result.FailureCode);
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenIsEmpty_FailsWithInvalidToken()
    {
        var validator = CreateValidator(CreateStrictMock<IProviderMetadataResolver>());

        var result = await validator.ValidateAsync(
            CreateRequest(Issuer, Audience, string.Empty),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.Equal(TokenValidationFailureCodes.InvalidToken, result.FailureCode);
    }

    [Fact]
    public async Task ValidateAsync_WhenDiscoveryIsUnavailable_FailsWithIssuerUnavailable()
    {
        var mockMetadata = CreateStrictMock<IProviderMetadataResolver>();
        mockMetadata
            .Setup(x => x.GetMetadataAsync(Issuer, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OpenIdProviderException("discovery unavailable"))
            .Verifiable();
        var validator = CreateValidator(mockMetadata);
        var token = await CreateValidAccessTokenAsync(CancellationToken.None);

        var result = await validator.ValidateAsync(
            CreateRequest(Issuer, Audience, token),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.True(result.IsUnavailable);
        Assert.Equal(TokenValidationFailureCodes.IssuerUnavailable, result.FailureCode);
    }

    [Fact]
    public async Task ValidateAsync_WhenKeySetIsUnavailable_FailsWithIssuerUnavailable()
    {
        var mockKeySet = CreateStrictMock<IProviderKeySetResolver>();
        mockKeySet
            .Setup(x => x.GetSigningKeysAsync(Issuer, It.IsAny<CancellationToken>()))
            .Throws(new OpenIdProviderException("jwks unavailable"))
            .Verifiable();
        var validator = CreateValidator(CreateMetadataMock(), mockKeySet);
        var token = await CreateValidAccessTokenAsync(CancellationToken.None);

        var result = await validator.ValidateAsync(
            CreateRequest(Issuer, Audience, token),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
        Assert.True(result.IsUnavailable);
        Assert.Equal(TokenValidationFailureCodes.IssuerUnavailable, result.FailureCode);
    }

    [Fact]
    public async Task ValidateAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var mockKeySet = CreateStrictMock<IProviderKeySetResolver>();
        mockKeySet
            .Setup(x => x.GetSigningKeysAsync(Issuer, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException())
            .Verifiable();
        var validator = CreateValidator(CreateMetadataMock(), mockKeySet);
        var token = await CreateValidAccessTokenAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(
                CreateRequest(Issuer, Audience, token),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ValidateAsync_WhenIssuerIsEmpty_ThrowsArgumentException()
    {
        var validator = CreateValidator(CreateStrictMock<IProviderMetadataResolver>());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await validator.ValidateAsync(
                CreateRequest(string.Empty, Audience, "token"),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ValidateAsync_WhenAudienceIsEmpty_ThrowsArgumentException()
    {
        var validator = CreateValidator(CreateStrictMock<IProviderMetadataResolver>());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await validator.ValidateAsync(
                CreateRequest(Issuer, string.Empty, "token"),
                CancellationToken.None
            )
        );
    }

    #endregion

    #region IssuerAccessTokenResult Tests

    [Fact]
    public void IsUnavailable_WhenFailureIsAnotherCode_IsFalse()
    {
        var result = new IssuerAccessTokenResult(
            TokenValidationFailureCodes.Expired,
            "The access token has expired."
        );

        Assert.False(result.IsValid);
        Assert.False(result.IsUnavailable);
    }

    #endregion
}
