// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Moq;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Jose.Credentials;
using NCode.Identity.Jwt;
using NCode.Identity.Jwt.Encoding;
using NCode.Identity.OpenId.Client;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.SharedState;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Tokens;

public sealed class DirectoryTokenIssuerTests : DefaultDirectoryStoreTestBase, IAsyncLifetime
{
    private const string DirectoryIssuer = "https://directory.example";
    private const string TokenEndpoint = "https://directory.example/directory/v1/token";
    private const string RingName = "directory";
    private const string ClientId = "cell-cell-a";

    private ServiceProvider ServiceProvider { get; }
    private RSA DirectoryRsa { get; } = RSA.Create(2048);
    private RSA CellRsa { get; } = RSA.Create(2048);
    private RSA OtherRsa { get; } = RSA.Create(2048);
    private SecretKey DirectoryKey { get; }
    private SecretKey CellKey { get; }
    private SecretKey OtherKey { get; }
    private FakeJwksReader JwksReader { get; } = new();
    private DirectoryTokenIssuer TokenIssuer { get; }
    private DirectoryClientAssertionValidator Validator { get; }
    private DirectoryTokenIssuer EmptyRingIssuer { get; }

    public DirectoryTokenIssuerTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection
            .AddSecretsLibrary()
            .AddJoseLibrary()
            .AddJwtLibrary()
            .AddOpenIdClientLibrary();
        serviceCollection.AddSharedStateServices().AddInProcessExpiringKeySet();
        ServiceProvider = serviceCollection.BuildServiceProvider();

        var factory = ServiceProvider.GetRequiredService<ISecretKeyFactory>();
        DirectoryKey = factory.CreateRsa(new KeyMetadata { KeyId = "dir" }, DirectoryRsa);
        CellKey = factory.CreateRsa(new KeyMetadata { KeyId = "cell-key" }, CellRsa);
        OtherKey = factory.CreateRsa(new KeyMetadata { KeyId = "other-key" }, OtherRsa);

        var collectionFactory = ServiceProvider.GetRequiredService<ISecretKeyCollectionFactory>();
        var ring = collectionFactory.Create([DirectoryKey]);
        var emptyRing = collectionFactory.Create([]);

        var options = Microsoft.Extensions.Options.Options.Create(
            new DirectoryServerOptions
            {
                Issuer = DirectoryIssuer,
                KeyRing = RingName,
                TokenLifetime = TimeSpan.FromMinutes(5),
            }
        );

        TokenIssuer = CreateIssuer(ring, options);
        EmptyRingIssuer = CreateIssuer(emptyRing, options);
        Validator = new DirectoryClientAssertionValidator(
            System.TimeProvider.System,
            CellRegistry,
            JwksReader,
            ServiceProvider.GetRequiredService<IAlgorithmCollectionProvider>(),
            ServiceProvider.GetRequiredService<NCode.Identity.Jwt.Validation.IJwtValidator>(),
            ServiceProvider.GetRequiredService<IExpiringKeySet>(),
            options
        );
    }

    private DirectoryTokenIssuer CreateIssuer(
        ISecretKeyCollection ring,
        IOptions<DirectoryServerOptions> options
    )
    {
        var ringProvider = new Mock<IClientKeyRingProvider>(MockBehavior.Strict);
        ringProvider.Setup(provider => provider.GetKeyRing(RingName)).Returns(ring);
        return new DirectoryTokenIssuer(
            System.TimeProvider.System,
            ringProvider.Object,
            ServiceProvider.GetRequiredService<IAlgorithmCollectionProvider>(),
            ServiceProvider.GetRequiredService<ICredentialSelector>(),
            ServiceProvider.GetRequiredService<IJwtEncoder>(),
            ServiceProvider.GetRequiredService<IIssuerAccessTokenValidator>(),
            options
        );
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
        DirectoryRsa.Dispose();
        CellRsa.Dispose();
        OtherRsa.Dispose();
    }

    private async ValueTask RegisterCellAsync(CancellationToken cancellationToken = default)
    {
        var registration = new CellRegistration
        {
            Name = "cell-a",
            Region = "region-a",
            Kind = CellKinds.Shared,
            Jwks = JsonElement.Parse("""{"keys":[]}"""),
        };
        await CellRegistry.RegisterCellAsync(registration, cancellationToken);
        JwksReader.Keys = [CellKey];
    }

    private async ValueTask<string> CreateAssertionAsync(
        SecretKey? signingKey = null,
        string issuer = ClientId,
        string subject = ClientId,
        string audience = TokenEndpoint,
        TimeSpan? expiresIn = null,
        string? jti = null,
        CancellationToken cancellationToken = default
    )
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(
            ServiceProvider
                .GetRequiredService<IAlgorithmCollectionProvider>()
                .Collection.TryGetSignatureAlgorithm("PS256", out var algorithm)
        );

        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["jti"] = jti ?? Guid.NewGuid().ToString("N"),
        };
        var parameters = new JwtEncodingParameters
        {
            SigningCredentials = new JoseSigningCredentials(signingKey ?? CellKey, algorithm),
            EnsureTokenLifetime = false,
            Issuer = issuer,
            Audience = new StringValues(audience),
            IssuedAt = now.AddMinutes(-1),
            NotBefore = now.AddMinutes(-1),
            Expires = now + (expiresIn ?? TimeSpan.FromMinutes(1)),
            ExtraPayloadClaims = claims,
        };
        return await ServiceProvider
            .GetRequiredService<IJwtEncoder>()
            .EncodeAsync(parameters, cancellationToken);
    }

    private async ValueTask<DirectoryClientAssertionValidator.Result> ValidateAsync(
        string assertion,
        CancellationToken cancellationToken = default
    ) => await Validator.ValidateAsync(assertion, TokenEndpoint, cancellationToken);

    #region IssueAsync and ValidateAsync Tests

    [Fact]
    public async Task IssueAsync_ThenValidateAsync_RoundTripsTheCellsToken()
    {
        var token = await TokenIssuer.IssueAsync("cell-a", CancellationToken.None);

        var result = await TokenIssuer.ValidateAsync(token, CancellationToken.None);

        Assert.True(result.IsValid, result.FailureReason);
        var payload = result.Payload;
        Assert.Equal(DirectoryIssuer, payload.GetProperty("iss").GetString());
        Assert.Equal("urn:ncode:directory", payload.GetProperty("aud").GetString());
        Assert.Equal("directory", payload.GetProperty("scope").GetString());
        Assert.Equal(ClientId, payload.GetProperty("client_id").GetString());
        Assert.Equal(ClientId, payload.GetProperty("sub").GetString());
        Assert.False(string.IsNullOrEmpty(payload.GetProperty("jti").GetString()));
        Assert.False(payload.TryGetProperty("cnf", out _));
        var lifetime =
            payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64();
        Assert.Equal(300, lifetime);
    }

    [Fact]
    public async Task ValidateAsync_WhenTheTokenIsSignedByAnotherKey_IsInvalid()
    {
        var other = new DirectoryTokenIssuerSigner(ServiceProvider, OtherKey);

        var token = await other.CreateAsync(DirectoryIssuer, "urn:ncode:directory");

        var result = await TokenIssuer.ValidateAsync(token, CancellationToken.None);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenTheAudienceIsWrong_IsInvalid()
    {
        var signer = new DirectoryTokenIssuerSigner(ServiceProvider, DirectoryKey);

        var token = await signer.CreateAsync(DirectoryIssuer, "urn:ncode:management");

        var result = await TokenIssuer.ValidateAsync(token, CancellationToken.None);
        Assert.False(result.IsValid);
        Assert.Equal(TokenValidationFailureCodes.InvalidAudience, result.FailureCode);
    }

    [Fact]
    public async Task IssueAsync_WhenTheKeyRingHasNoKey_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await EmptyRingIssuer.IssueAsync("cell-a", CancellationToken.None)
        );
    }

    #endregion

    #region DirectoryClientAssertionValidator Tests

    [Fact]
    public async Task ValidateAssertionAsync_WhenTheAssertionIsGood_NamesTheCell()
    {
        await RegisterCellAsync();
        var assertion = await CreateAssertionAsync();

        var result = await ValidateAsync(assertion);

        Assert.True(result.IsValid, result.FailureReason);
        Assert.Equal("cell-a", result.CellName);
    }

    [Fact]
    public async Task ValidateAssertionAsync_WhenTheAudienceIsTheIssuer_NamesTheCell()
    {
        await RegisterCellAsync();
        var assertion = await CreateAssertionAsync(audience: DirectoryIssuer);

        var result = await ValidateAsync(assertion);

        Assert.True(result.IsValid, result.FailureReason);
    }

    [Fact]
    public async Task ValidateAssertionAsync_WhenTheAudienceIsWrong_IsRefused()
    {
        await RegisterCellAsync();
        var assertion = await CreateAssertionAsync(audience: "https://other.example/token");

        var result = await ValidateAsync(assertion);

        Assert.False(result.IsValid);
        Assert.Contains("'aud'", result.FailureReason);
    }

    [Fact]
    public async Task ValidateAssertionAsync_WhenTheJtiIsReplayed_IsRefused()
    {
        await RegisterCellAsync();
        var assertion = await CreateAssertionAsync(jti: "same");
        var replay = await CreateAssertionAsync(jti: "same");

        var first = await ValidateAsync(assertion);
        var second = await ValidateAsync(replay);

        Assert.True(first.IsValid, first.FailureReason);
        Assert.False(second.IsValid);
    }

    [Fact]
    public async Task ValidateAssertionAsync_WhenTheAssertionIsExpired_IsRefused()
    {
        await RegisterCellAsync();
        var assertion = await CreateAssertionAsync(expiresIn: TimeSpan.FromMinutes(-10));

        var result = await ValidateAsync(assertion);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAssertionAsync_WhenTheExpiryIsTooFarAhead_IsRefused()
    {
        await RegisterCellAsync();
        var assertion = await CreateAssertionAsync(expiresIn: TimeSpan.FromMinutes(10));

        var result = await ValidateAsync(assertion);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAssertionAsync_WhenTheCellIsUnknown_IsRefused()
    {
        JwksReader.Keys = [CellKey];
        var assertion = await CreateAssertionAsync();

        var result = await ValidateAsync(assertion);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAssertionAsync_WhenTheCellIsRetired_IsRefused()
    {
        await RegisterCellAsync();
        await CellRegistry.RetireCellAsync("cell-a", CancellationToken.None);
        var assertion = await CreateAssertionAsync();

        var result = await ValidateAsync(assertion);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAssertionAsync_WhenAnotherKeySigns_IsRefused()
    {
        await RegisterCellAsync();
        var assertion = await CreateAssertionAsync(signingKey: OtherKey);

        var result = await ValidateAsync(assertion);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAssertionAsync_WhenTheSubjectIsNotTheIssuer_IsRefused()
    {
        await RegisterCellAsync();
        var assertion = await CreateAssertionAsync(subject: "cell-cell-b");

        var result = await ValidateAsync(assertion);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task ValidateAssertionAsync_WhenTheAssertionIsMalformed_IsRefused(string assertion)
    {
        await RegisterCellAsync();

        var result = await ValidateAsync(assertion);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAssertionAsync_WhenTheIssuerIsNotACellClient_IsRefused()
    {
        await RegisterCellAsync();
        var assertion = await CreateAssertionAsync(issuer: "other-a", subject: "other-a");

        var result = await ValidateAsync(assertion);

        Assert.False(result.IsValid);
    }

    #endregion

    private sealed class DirectoryTokenIssuerSigner(IServiceProvider serviceProvider, SecretKey key)
    {
        public async ValueTask<string> CreateAsync(
            string issuer,
            string audience,
            CancellationToken cancellationToken = default
        )
        {
            Assert.True(
                serviceProvider
                    .GetRequiredService<IAlgorithmCollectionProvider>()
                    .Collection.TryGetSignatureAlgorithm("PS256", out var algorithm)
            );
            var now = DateTimeOffset.UtcNow;
            var parameters = new JwtEncodingParameters
            {
                SigningCredentials = new JoseSigningCredentials(key, algorithm),
                TokenType = JoseTokenTypes.AccessTokenJwt,
                EnsureTokenLifetime = false,
                Issuer = issuer,
                Audience = new StringValues(audience),
                IssuedAt = now,
                Expires = now.AddMinutes(2),
            };
            return await serviceProvider
                .GetRequiredService<IJwtEncoder>()
                .EncodeAsync(parameters, cancellationToken);
        }
    }

    private sealed class FakeJwksReader : IPublicJwksReader
    {
        public IReadOnlyList<SecretKey> Keys { get; set; } = [];

        public bool TryRead(
            JsonElement jwks,
            [NotNullWhen(true)] out IReadOnlyList<SecretKey>? keys,
            [NotNullWhen(false)] out string? error
        )
        {
            keys = Keys;
            error = null;
            return true;
        }
    }
}
