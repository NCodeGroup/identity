// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Buffers;
using NCode.Identity.Jose.DPoP;
using NCode.Identity.Jwk;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.GlobalDirectory.ControlPlane;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.SharedState;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class ReplicatedManagementTokenValidatorTests : BaseTests
{
    private const string Issuer = "https://control-plane.example.test";
    private const string ManagementUrl = "https://cell-a.example.test/ignored";
    private const string CellName = "cell-a";
    private const string Audience = "urn:ncode:management";
    private const string AccessToken = "access-token";
    private const string ProofJwt = "header.payload.signature";
    private const string Thumbprint = "thumbprint";
    private const string Jti = "jti-1";
    private const string ExpectedHttpUri = "https://cell-a.example.test/base/api/tenants";

    private static DateTimeOffset IssuedAt { get; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static TimeSpan Lifetime { get; } = TimeSpan.FromSeconds(60);

    private Mock<IIssuerAccessTokenValidator> AccessTokenValidatorMock { get; }
    private Mock<IControlPlaneReplica> ReplicaMock { get; }
    private Mock<ISecretKeyCollectionFactory> KeyCollectionFactoryMock { get; }
    private Mock<ISecretKeyCollection> SigningKeysMock { get; }
    private Mock<IDPoPProofService> ProofServiceMock { get; }
    private Mock<IExpiringKeySet> ReplayKeysMock { get; }

    public ReplicatedManagementTokenValidatorTests()
    {
        AccessTokenValidatorMock = CreateStrictMock<IIssuerAccessTokenValidator>();
        ReplicaMock = CreateStrictMock<IControlPlaneReplica>();
        KeyCollectionFactoryMock = CreateStrictMock<ISecretKeyCollectionFactory>();
        SigningKeysMock = CreateStrictMock<ISecretKeyCollection>();
        ProofServiceMock = CreateStrictMock<IDPoPProofService>();
        ReplayKeysMock = CreateStrictMock<IExpiringKeySet>();
    }

    #region Scaffolding

    private ReplicatedManagementTokenValidator CreateValidator(
        string issuer = Issuer,
        string managementUrl = ManagementUrl
    ) =>
        new(
            AccessTokenValidatorMock.Object,
            ReplicaMock.Object,
            KeyCollectionFactoryMock.Object,
            ProofServiceMock.Object,
            ReplayKeysMock.Object,
            Options.Create(
                new CellOptions
                {
                    Name = CellName,
                    ManagementUrl = managementUrl,
                    ControlPlane = new ControlPlaneOptions { Issuer = issuer },
                }
            )
        );

    private static DefaultHttpContext CreateHttpContext(
        string? authorization,
        params string[] proofs
    )
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Host = new HostString("evil.example.test");
        httpContext.Request.PathBase = "/base";
        httpContext.Request.Path = "/api/tenants";
        if (authorization is not null)
            httpContext.Request.Headers.Authorization = authorization;
        if (proofs.Length > 0)
            httpContext.Request.Headers["DPoP"] = proofs;
        return httpContext;
    }

    private static IssuerAccessTokenResult CreateValid(string payloadJson) =>
        new(JsonElement.Parse(payloadJson), Issuer);

    private static string CreateBoundPayload(string thumbprint = Thumbprint) =>
        $$$"""{"sub":"user-1","client_id":"client-1","scope":"a b","cnf":{"jkt":"{{{thumbprint}}}"}}""";

    private static DPoPProof CreateProof(string thumbprint = Thumbprint, string jti = Jti) =>
        new()
        {
            Algorithm = "ES256",
            PublicKey = new JsonWebKey(
                JsonElement.Parse("""{"kty":"EC","crv":"P-256","x":"x","y":"y"}""")
            ),
            JwkThumbprint = thumbprint,
            Jti = jti,
            IssuedAt = IssuedAt,
        };

    private void SetupReplica(bool hasKeys = true, params string[] revokedKeyIds)
    {
        var keys = new ReplicatedManagementKeys
        {
            HasKeys = hasKeys,
            Keys = [],
            RevokedKeyIds = new HashSet<string>(revokedKeyIds, StringComparer.Ordinal),
        };

        ReplicaMock
            .Setup(x => x.GetManagementKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(keys)
            .Verifiable(Times.Once);
    }

    private void SetupAccessTokenValidator(string token, IssuerAccessTokenResult result)
    {
        KeyCollectionFactoryMock
            .Setup(x => x.Create(It.IsAny<IEnumerable<SecretKey>>()))
            .Returns(SigningKeysMock.Object)
            .Verifiable(Times.Once);

        AccessTokenValidatorMock
            .Setup(x =>
                x.ValidateAsync(
                    It.Is<IssuerAccessTokenValidationRequest>(request =>
                        request.Issuer == Issuer
                        && request.Audience == Audience
                        && request.Token == token
                        && ReferenceEquals(request.SigningKeys, SigningKeysMock.Object)
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(result)
            .Verifiable(Times.Once);
    }

    private void SetupAccessTokenValidator(IssuerAccessTokenResult result) =>
        SetupAccessTokenValidator(AccessToken, result);

    private static string CreateTokenWithKeyId(string keyId)
    {
        var header = Base64Url.EncodeToString(
            SecureEncoding.UTF8.GetBytes($$"""{"alg":"ES256","kid":"{{keyId}}"}""")
        );
        return $"{header}.payload.signature";
    }

    private void SetupProofService(DPoPProof? proof, string? error = null) =>
        ProofServiceMock
            .Setup(x =>
                x.TryValidateProof(
                    ProofJwt,
                    It.Is<DPoPProofValidationParameters>(p =>
                        p.HttpMethod == "POST"
                        && p.HttpUri == ExpectedHttpUri
                        && p.AccessToken == AccessToken
                        && p.Lifetime == Lifetime
                        && p.AllowedAlgorithms.SequenceEqual(
                            new[] { "ES256", "ES384", "ES512", "PS256", "PS384", "PS512", "EdDSA" }
                        )
                    ),
                    out proof,
                    out error
                )
            )
            .Returns(proof is not null)
            .Verifiable(Times.Once);

    private void SetupReplayKeys(bool added)
    {
        var expectedKey = ExpiringKeys.Create("management_dpop_jti", CellName, Thumbprint, Jti);

        ReplayKeysMock
            .Setup(x =>
                x.TryAddAsync(expectedKey, IssuedAt + Lifetime, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(added)
            .Verifiable(Times.Once);
    }

    #endregion

    #region ValidateAsync Tests

    [Fact]
    public async Task ValidateAsync_WhenReplicaHoldsNoKeys_ReturnsUnavailableWithRetryAfter()
    {
        SetupReplica(hasKeys: false);

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
        Assert.True(validation.IsUnavailable);
        Assert.Equal(
            TimeSpan.FromSeconds(GlobalDirectoryProtocol.UnavailableRetryAfterSeconds),
            validation.RetryAfter
        );
    }

    [Fact]
    public async Task ValidateAsync_WithBearerScheme_ReturnsUnauthenticated()
    {
        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"Bearer {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
        Assert.False(validation.IsUnavailable);
    }

    [Fact]
    public async Task ValidateAsync_WithUnboundToken_ReturnsUnauthenticated()
    {
        SetupReplica();
        SetupAccessTokenValidator(
            CreateValid("""{"sub":"user-1","client_id":"client-1","scope":"a b"}""")
        );

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
        Assert.False(validation.IsUnavailable);
    }

    [Fact]
    public async Task ValidateAsync_WithRevokedKeyId_ReturnsUnauthenticated()
    {
        SetupReplica(true, "kid-1");
        var token = CreateTokenWithKeyId("kid-1");

        var validation = await CreateValidator()
            .ValidateAsync(CreateHttpContext($"DPoP {token}", ProofJwt), CancellationToken.None);

        Assert.False(validation.IsAuthenticated);
        Assert.False(validation.IsUnavailable);
    }

    [Fact]
    public async Task ValidateAsync_WithOnlyClientId_ReturnsAuthenticatedPrincipalWithoutSubject()
    {
        SetupReplica();
        SetupAccessTokenValidator(
            CreateValid($$$"""{"client_id":"client-1","cnf":{"jkt":"{{{Thumbprint}}}"}}""")
        );
        SetupProofService(CreateProof());
        SetupReplayKeys(added: true);

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.True(validation.IsAuthenticated);
        var identity = Assert.IsType<ClaimsIdentity>(validation.Principal?.Identity);
        Assert.Null(identity.FindFirst("sub"));
        Assert.Equal("client-1", identity.FindFirst("client_id")?.Value);
    }

    [Fact]
    public async Task ValidateAsync_WithoutSubjectAndClientId_ReturnsUnauthenticated()
    {
        SetupReplica();
        SetupAccessTokenValidator(
            CreateValid($$$"""{"scope":"a","cnf":{"jkt":"{{{Thumbprint}}}"}}""")
        );
        SetupProofService(CreateProof());
        SetupReplayKeys(added: true);

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
        Assert.False(validation.IsUnavailable);
    }

    [Fact]
    public async Task ValidateAsync_WithUnknownKeyOrWrongAudience_ReturnsUnauthenticated()
    {
        SetupReplica();
        SetupAccessTokenValidator(
            new IssuerAccessTokenResult("invalid_signature", "The key is unknown.")
        );

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
        Assert.False(validation.IsUnavailable);
    }

    [Fact]
    public async Task ValidateAsync_WithoutAuthorizationHeader_ReturnsUnauthenticated()
    {
        var validation = await CreateValidator()
            .ValidateAsync(CreateHttpContext(null), CancellationToken.None);

        Assert.False(validation.IsAuthenticated);
        Assert.False(validation.IsUnavailable);
    }

    [Fact]
    public async Task ValidateAsync_WithEmptyIssuer_ReturnsUnauthenticated()
    {
        var validation = await CreateValidator(issuer: "")
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
        Assert.False(validation.IsUnavailable);
    }

    [Fact]
    public async Task ValidateAsync_WithBoundTokenAndNoProof_ReturnsUnauthenticated()
    {
        SetupReplica();
        SetupAccessTokenValidator(CreateValid(CreateBoundPayload()));

        var validation = await CreateValidator()
            .ValidateAsync(CreateHttpContext($"DPoP {AccessToken}"), CancellationToken.None);

        Assert.False(validation.IsAuthenticated);
    }

    [Fact]
    public async Task ValidateAsync_WithBoundTokenAndTwoProofs_ReturnsUnauthenticated()
    {
        SetupReplica();
        SetupAccessTokenValidator(CreateValid(CreateBoundPayload()));

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt, ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
    }

    [Fact]
    public async Task ValidateAsync_WithBoundTokenAndNoManagementUrl_ReturnsUnauthenticated()
    {
        SetupReplica();
        SetupAccessTokenValidator(CreateValid(CreateBoundPayload()));

        var validation = await CreateValidator(managementUrl: "")
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
    }

    [Fact]
    public async Task ValidateAsync_WithBoundTokenAndManagementUrlWithSurroundingWhitespace_ReturnsUnauthenticated()
    {
        SetupReplica();
        SetupAccessTokenValidator(CreateValid(CreateBoundPayload()));

        var validation = await CreateValidator(
                managementUrl: " https://cell-a.example.test/ignored"
            )
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
    }

    [Fact]
    public async Task ValidateAsync_WithProofTheServiceRejects_ReturnsUnauthenticated()
    {
        SetupReplica();
        SetupAccessTokenValidator(CreateValid(CreateBoundPayload()));
        SetupProofService(null, "The proof is invalid.");

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
    }

    [Fact]
    public async Task ValidateAsync_WithProofMadeWithAnotherKey_ReturnsUnauthenticated()
    {
        SetupReplica();
        SetupAccessTokenValidator(CreateValid(CreateBoundPayload()));
        SetupProofService(CreateProof("other-thumbprint"));

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
    }

    [Fact]
    public async Task ValidateAsync_WithReplayedProof_ReturnsUnauthenticated()
    {
        SetupReplica();
        SetupAccessTokenValidator(CreateValid(CreateBoundPayload()));
        SetupProofService(CreateProof());
        SetupReplayKeys(added: false);

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.False(validation.IsAuthenticated);
    }

    [Fact]
    public async Task ValidateAsync_WithBoundTokenAndValidProof_ReturnsAuthenticatedPrincipal()
    {
        SetupReplica();
        SetupAccessTokenValidator(CreateValid(CreateBoundPayload()));
        SetupProofService(CreateProof());
        SetupReplayKeys(added: true);

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.True(validation.IsAuthenticated);
        var identity = Assert.IsType<ClaimsIdentity>(validation.Principal?.Identity);
        Assert.Equal("user-1", identity.FindFirst("sub")?.Value);
        Assert.Equal("client-1", identity.FindFirst("client_id")?.Value);
        Assert.Equal("a b", Assert.Single(identity.FindAll("scope")).Value);
    }

    [Fact]
    public async Task ValidateAsync_WhenTheProofsJtiIsLong_RecordsAKeyOfBoundedLength()
    {
        SetupReplica();
        SetupAccessTokenValidator(CreateValid(CreateBoundPayload()));
        SetupProofService(CreateProof(jti: new string('j', 4_000)));
        ReplayKeysMock
            .Setup(x =>
                x.TryAddAsync(
                    It.Is<string>(key => key.Length < 100),
                    IssuedAt + Lifetime,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable(Times.Once);

        var validation = await CreateValidator()
            .ValidateAsync(
                CreateHttpContext($"DPoP {AccessToken}", ProofJwt),
                CancellationToken.None
            );

        Assert.True(validation.IsAuthenticated);
    }

    #endregion
}
