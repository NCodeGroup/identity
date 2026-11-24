// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.Client;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DefaultCellTokenSourceTests : BaseTests
{
    private const string Issuer = "https://control-plane.example.test";
    private const string ClientId = "cell-a";
    private const string KeyRing = "cell-a-ring";
    private const string DirectoryAudience = "urn:ncode:directory";
    private const string DirectoryScope = "directory";
    private const string OtherAudience = "urn:example:other-api";
    private const string OtherScope = "other-api";

    private static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(10);

    private static TimeSpan FailureCacheDuration { get; } = TimeSpan.FromSeconds(2);

    // Bounds a wait that only a defect can leave open, so that a failing run ends; a passing run never reaches it.
    private static TimeSpan HangGuard { get; } = TimeSpan.FromSeconds(10);

    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private Mock<ITokenClient> TokenClientMock { get; }

    private DefaultCellTokenSource TokenSource { get; }

    public DefaultCellTokenSourceTests()
    {
        TokenClientMock = CreateStrictMock<ITokenClient>();
        TokenSource = new DefaultCellTokenSource(
            TokenClientMock.Object,
            Options.Create(
                new DirectoryClientOptions
                {
                    ClientId = ClientId,
                    KeyRing = KeyRing,
                    TokenRequestTimeout = RequestTimeout,
                    TokenFailureCacheDuration = FailureCacheDuration,
                }
            ),
            Options.Create(
                new CellOptions { ControlPlane = new ControlPlaneOptions { Issuer = Issuer } }
            ),
            TimeProvider
        );
    }

    private static TokenResult CreateSuccess(string accessToken, int? expiresIn) =>
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
    public async Task GetAccessTokenAsync_WhenCalled_RequestsTheGivenAudienceAndScope()
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

        var token = await TokenSource.GetAccessTokenAsync(
            DirectoryAudience,
            DirectoryScope,
            CancellationToken.None
        );

        Assert.Equal("token-1", token);
        Assert.NotNull(request);
        Assert.IsType<ClientCredentialsTokenRequest>(request);
        Assert.Equal([DirectoryAudience], request.Resources);
        Assert.Equal(DirectoryScope, request.Scope);
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

        var first = await TokenSource.GetAccessTokenAsync(
            DirectoryAudience,
            DirectoryScope,
            CancellationToken.None
        );
        TimeProvider.Advance(TimeSpan.FromSeconds(200));
        var second = await TokenSource.GetAccessTokenAsync(
            DirectoryAudience,
            DirectoryScope,
            CancellationToken.None
        );

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

        var first = await TokenSource.GetAccessTokenAsync(
            DirectoryAudience,
            DirectoryScope,
            CancellationToken.None
        );
        TimeProvider.Advance(TimeSpan.FromSeconds(280));
        var second = await TokenSource.GetAccessTokenAsync(
            DirectoryAudience,
            DirectoryScope,
            CancellationToken.None
        );

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTwoAudiencesAreAsked_RequestsAndCachesEachToken()
    {
        var requests = new List<TokenRequest>();
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (ClientRegistration _, TokenRequest t, CancellationToken _) =>
                {
                    requests.Add(t);
                    return new ValueTask<TokenResult>(
                        CreateSuccess($"token-{requests.Count}", 300)
                    );
                }
            )
            .Verifiable(Times.Exactly(2));

        var directory = await TokenSource.GetAccessTokenAsync(
            DirectoryAudience,
            DirectoryScope,
            CancellationToken.None
        );
        var controlPlane = await TokenSource.GetAccessTokenAsync(
            OtherAudience,
            OtherScope,
            CancellationToken.None
        );
        var directoryAgain = await TokenSource.GetAccessTokenAsync(
            DirectoryAudience,
            DirectoryScope,
            CancellationToken.None
        );
        var controlPlaneAgain = await TokenSource.GetAccessTokenAsync(
            OtherAudience,
            OtherScope,
            CancellationToken.None
        );

        Assert.Equal("token-1", directory);
        Assert.Equal("token-2", controlPlane);
        Assert.Equal(directory, directoryAgain);
        Assert.Equal(controlPlane, controlPlaneAgain);
        Assert.Equal(2, requests.Count);
        Assert.Equal([DirectoryAudience], requests[0].Resources);
        Assert.Equal(DirectoryScope, requests[0].Scope);
        Assert.Equal([OtherAudience], requests[1].Resources);
        Assert.Equal(OtherScope, requests[1].Scope);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhileAnotherAudienceIsRequested_ReturnsTheCachedToken()
    {
        var pending = new TaskCompletionSource<TokenResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.Is<TokenRequest>(r => r.Scope == OtherScope),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<TokenResult>(CreateSuccess("control-plane-token", 300)))
            .Verifiable(Times.Once);
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.Is<TokenRequest>(r => r.Scope == DirectoryScope),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<TokenResult>(pending.Task))
            .Verifiable(Times.Once);

        var cached = await TokenSource.GetAccessTokenAsync(
            OtherAudience,
            OtherScope,
            CancellationToken.None
        );
        var directoryTask = TokenSource
            .GetAccessTokenAsync(DirectoryAudience, DirectoryScope, CancellationToken.None)
            .AsTask();

        var controlPlaneTask = TokenSource
            .GetAccessTokenAsync(OtherAudience, OtherScope, CancellationToken.None)
            .AsTask();

        Assert.True(controlPlaneTask.IsCompletedSuccessfully);
        Assert.False(directoryTask.IsCompleted);
        Assert.Equal(cached, await controlPlaneTask);

        pending.SetResult(CreateSuccess("directory-token", 300));
        Assert.Equal("directory-token", await directoryTask);
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

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(OtherAudience, OtherScope, CancellationToken.None)
        );

        Assert.Contains(OtherAudience, exception.Message);
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
            await TokenSource.GetAccessTokenAsync(
                DirectoryAudience,
                DirectoryScope,
                CancellationToken.None
            )
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetAccessTokenAsync_WhenTheResponseHasNoPositiveExpiresIn_ThrowsDirectoryUnavailable(
        int? expiresIn
    )
    {
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<TokenResult>(CreateSuccess("token-1", expiresIn)))
            .Verifiable(Times.Once);

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(
                DirectoryAudience,
                DirectoryScope,
                CancellationToken.None
            )
        );

        Assert.Contains(DirectoryAudience, exception.Message);
        Assert.Contains(OpenIdProtocol.Parameters.ExpiresIn, exception.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheFirstCallerCancels_TheSharedRequestCompletesForTheOthers()
    {
        var pending = new TaskCompletionSource<TokenResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var requestTokens = new List<CancellationToken>();
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (ClientRegistration _, TokenRequest _, CancellationToken token) =>
                {
                    requestTokens.Add(token);
                    return new ValueTask<TokenResult>(pending.Task.WaitAsync(token));
                }
            )
            .Verifiable(Times.Once);
        using var firstCaller = new CancellationTokenSource();

        var first = TokenSource
            .GetAccessTokenAsync(DirectoryAudience, DirectoryScope, firstCaller.Token)
            .AsTask();
        var second = TokenSource
            .GetAccessTokenAsync(DirectoryAudience, DirectoryScope, CancellationToken.None)
            .AsTask();
        await firstCaller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(HangGuard));
        var requestToken = Assert.Single(requestTokens);
        Assert.NotEqual(firstCaller.Token, requestToken);
        Assert.False(requestToken.IsCancellationRequested);
        Assert.False(second.IsCompleted);

        pending.SetResult(CreateSuccess("token-1", 300));
        Assert.Equal("token-1", await second.WaitAsync(HangGuard));
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheRequestOutlastsItsTimeLimit_ThrowsDirectoryUnavailable()
    {
        var pending = new TaskCompletionSource<TokenResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (ClientRegistration _, TokenRequest _, CancellationToken token) =>
                {
                    token.Register(() => pending.TrySetCanceled(token));
                    return new ValueTask<TokenResult>(pending.Task);
                }
            )
            .Verifiable(Times.Once);

        var call = TokenSource
            .GetAccessTokenAsync(DirectoryAudience, DirectoryScope, CancellationToken.None)
            .AsTask();
        TimeProvider.Advance(RequestTimeout);

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            call.WaitAsync(HangGuard)
        );
        Assert.Contains(DirectoryAudience, exception.Message);
        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(
                DirectoryAudience,
                DirectoryScope,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithinTheFailureWindow_ThrowsWithoutRequesting()
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
            await TokenSource.GetAccessTokenAsync(
                DirectoryAudience,
                DirectoryScope,
                CancellationToken.None
            )
        );
        TimeProvider.Advance(FailureCacheDuration - TimeSpan.FromMilliseconds(1));

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(
                DirectoryAudience,
                DirectoryScope,
                CancellationToken.None
            )
        );

        Assert.Contains("failed moments ago", exception.Message);
        Assert.Contains(DirectoryAudience, exception.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_AfterTheFailureWindow_RequestsAgain()
    {
        var error = new TokenResult(new ProtocolError("invalid_client", 401));
        var requests = 0;
        TokenClientMock
            .Setup(x =>
                x.RequestTokenAsync(
                    It.IsAny<ClientRegistration>(),
                    It.IsAny<TokenRequest>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() =>
                new ValueTask<TokenResult>(++requests == 1 ? error : CreateSuccess("token-2", 300))
            )
            .Verifiable(Times.Exactly(2));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(
                DirectoryAudience,
                DirectoryScope,
                CancellationToken.None
            )
        );
        TimeProvider.Advance(FailureCacheDuration);

        var token = await TokenSource.GetAccessTokenAsync(
            DirectoryAudience,
            DirectoryScope,
            CancellationToken.None
        );

        Assert.Equal("token-2", token);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheCallersTokenIsAlreadyCancelled_SendsNoRequest()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await TokenSource.GetAccessTokenAsync(
                DirectoryAudience,
                DirectoryScope,
                cancellation.Token
            )
        );
    }

    #endregion
}
