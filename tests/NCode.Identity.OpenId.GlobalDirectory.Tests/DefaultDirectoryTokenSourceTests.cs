// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.Client;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DefaultDirectoryTokenSourceTests : BaseTests
{
    private const string Issuer = "https://control-plane.example.test";
    private const string ClientId = "cell-a";
    private const string KeyRing = "cell-a-ring";

    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private Mock<ITokenClient> TokenClientMock { get; }

    private DefaultDirectoryTokenSource TokenSource { get; }

    public DefaultDirectoryTokenSourceTests()
    {
        TokenClientMock = CreateStrictMock<ITokenClient>();
        TokenSource = new DefaultDirectoryTokenSource(
            TokenClientMock.Object,
            Options.Create(new DirectoryClientOptions { ClientId = ClientId, KeyRing = KeyRing }),
            Options.Create(
                new CellOptions { ControlPlane = new ControlPlaneOptions { Issuer = Issuer } }
            ),
            TimeProvider
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            TokenSource.Dispose();

        base.Dispose(disposing);
    }

    private static TokenResult CreateSuccess(string accessToken, int expiresIn) =>
        new(
            new TokenResponse
            {
                AccessToken = accessToken,
                TokenType = "Bearer",
                ExpiresIn = expiresIn,
            }
        );

    #region GetAccessTokenAsync Tests

    [Fact]
    public async Task GetAccessTokenAsync_WhenCalled_RequestsTheDirectoryAudienceAndScope()
    {
        ClientRegistration? registration = null;
        TokenRequest? request = null;
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (ClientRegistration r, TokenRequest t, CancellationToken _) =>
                {
                    registration = r;
                    request = t;
                    return new ValueTask<TokenResult>(CreateSuccess("token-1", 300));
                }
            )
            .Verifiable(Times.Once);

        var token = await TokenSource.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", token);
        Assert.NotNull(request);
        Assert.IsType<ClientCredentialsTokenRequest>(request);
        Assert.Equal(["urn:ncode:directory"], request.Resources);
        Assert.Equal("directory", request.Scope);
        Assert.NotNull(registration);
        Assert.Equal(Issuer, registration.Authority);
        Assert.Equal(ClientId, registration.ClientId);
        Assert.Equal(KeyRing, registration.KeyRing);
        Assert.Equal(
            OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt,
            registration.AuthenticationMethod
        );
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheTokenIsStillFresh_ReusesTheToken()
    {
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<TokenResult>(CreateSuccess("token-1", 300)))
            .Verifiable(Times.Once);

        var first = await TokenSource.GetAccessTokenAsync(CancellationToken.None);
        TimeProvider.Advance(TimeSpan.FromSeconds(200));
        var second = await TokenSource.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", first);
        Assert.Equal("token-1", second);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheTokenIsNearExpiry_RequestsANewToken()
    {
        var requests = 0;
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() => new ValueTask<TokenResult>(CreateSuccess($"token-{++requests}", 300)))
            .Verifiable(Times.Exactly(2));

        var first = await TokenSource.GetAccessTokenAsync(CancellationToken.None);
        TimeProvider.Advance(TimeSpan.FromSeconds(280));
        var second = await TokenSource.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheControlPlaneRefusesTheRequest_ThrowsDirectoryUnavailable()
    {
        var error = new TokenResult(new ProtocolError("invalid_client", 401));
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<TokenResult>(error))
            .Verifiable(Times.Once);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheResponseHasNoAccessToken_ThrowsDirectoryUnavailable()
    {
        var response = new TokenResult(new TokenResponse { TokenType = "Bearer", ExpiresIn = 300 });
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<TokenResult>(response))
            .Verifiable(Times.Once);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
        );
    }

    #endregion
}
