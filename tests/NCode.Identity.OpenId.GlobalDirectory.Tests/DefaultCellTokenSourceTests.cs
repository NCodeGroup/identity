// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NCode.Buffers;
using NCode.Identity.OpenId.Client;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DefaultCellTokenSourceTests : BaseTests
{
    private const string DirectoryUrl = "https://directory.example.test";
    private const string TokenEndpoint = "https://directory.example.test/directory/v1/token";
    private const string ClientId = "cell-a";
    private const string KeyRing = "cell-a-ring";

    private static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(10);

    private static TimeSpan FailureCacheDuration { get; } = TimeSpan.FromSeconds(2);

    // Bounds a wait that only a defect can leave open, so that a failing run ends; a passing run never reaches it.
    private static TimeSpan HangGuard { get; } = TimeSpan.FromSeconds(10);

    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private StubHandler Handler { get; } = new();

    private FakeAuthenticator Authenticator { get; } = new();

    private DefaultCellTokenSource TokenSource { get; }

    public DefaultCellTokenSourceTests()
    {
        var factoryMock = CreateStrictMock<IHttpClientFactory>();
        factoryMock
            .Setup(x => x.CreateClient(DirectoryClientDefaults.HttpClientName))
            .Returns(() => new HttpClient(Handler, disposeHandler: false));

        TokenSource = new DefaultCellTokenSource(
            factoryMock.Object,
            [Authenticator],
            Options.Create(
                new DirectoryClientOptions
                {
                    Url = DirectoryUrl,
                    ClientId = ClientId,
                    KeyRing = KeyRing,
                    TokenRequestTimeout = RequestTimeout,
                    TokenFailureCacheDuration = FailureCacheDuration,
                }
            ),
            TimeProvider
        );
    }

    private static HttpResponseMessage CreateSuccess(string accessToken, int? expiresIn)
    {
        var expiresInText = expiresIn is null ? string.Empty : $",\"expires_in\":{expiresIn}";
        var json =
            $"{{\"access_token\":\"{accessToken}\",\"token_type\":\"Bearer\"{expiresInText}}}";
        return Json(HttpStatusCode.OK, json);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, SecureEncoding.UTF8, "application/json") };

    #region GetAccessTokenAsync Tests

    [Fact]
    public async Task GetAccessTokenAsync_WhenCalled_PostsTheClientCredentialsGrantToTheDirectoryTokenEndpoint()
    {
        Handler.Respond = (_, _) => Task.FromResult(CreateSuccess("token-1", 300));

        var token = await TokenSource.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", token);
        var request = Assert.Single(Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(TokenEndpoint, request.Uri);
        Assert.Contains(
            new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.GrantType,
                OpenIdProtocol.GrantTypes.ClientCredentials
            ),
            request.Form
        );
        Assert.Contains(
            new KeyValuePair<string, string>(
                OpenIdProtocol.Parameters.ClientAssertion,
                "assertion"
            ),
            request.Form
        );

        var context = Assert.Single(Authenticator.Contexts);
        Assert.Equal(ClientId, context.Registration.ClientId);
        Assert.Equal(KeyRing, context.Registration.KeyRing);
        Assert.Equal(DirectoryUrl, context.Registration.Authority);
        Assert.Equal(
            OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt,
            context.Registration.AuthenticationMethod
        );
        Assert.Equal(DirectoryUrl, context.Metadata.Issuer);
        Assert.Equal(TokenEndpoint, context.Metadata.TokenEndpoint);
        Assert.Equal(TokenEndpoint, context.Endpoint);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheTokenIsStillFresh_ReusesTheToken()
    {
        Handler.Respond = (_, _) => Task.FromResult(CreateSuccess("token-1", 300));

        var first = await TokenSource.GetAccessTokenAsync(CancellationToken.None);
        TimeProvider.Advance(TimeSpan.FromSeconds(200));
        var second = await TokenSource.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", first);
        Assert.Equal("token-1", second);
        Assert.Single(Handler.Requests);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheTokenIsNearExpiry_RequestsANewToken()
    {
        var requests = 0;
        Handler.Respond = (_, _) =>
            Task.FromResult(CreateSuccess($"token-{Interlocked.Increment(ref requests)}", 300));

        var first = await TokenSource.GetAccessTokenAsync(CancellationToken.None);
        TimeProvider.Advance(TimeSpan.FromSeconds(280));
        var second = await TokenSource.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(2, Handler.Requests.Count);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheDirectoryAnswersInvalidClient_ThrowsClientNotRegistered()
    {
        Handler.Respond = (_, _) =>
            Task.FromResult(Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}"));

        await Assert.ThrowsAsync<DirectoryClientNotRegisteredException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheDirectoryRefusesOtherwise_ThrowsDirectoryUnavailable()
    {
        Handler.Respond = (_, _) =>
            Task.FromResult(Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_request\"}"));

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
        );

        Assert.IsNotType<DirectoryClientNotRegisteredException>(exception);
        Assert.Contains("invalid_request", exception.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheEndpointCannotBeReached_ThrowsDirectoryUnavailable()
    {
        Handler.Respond = (_, _) => throw new HttpRequestException("connection refused");

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheResponseHasNoAccessToken_ThrowsDirectoryUnavailable()
    {
        Handler.Respond = (_, _) =>
            Task.FromResult(
                Json(HttpStatusCode.OK, "{\"token_type\":\"Bearer\",\"expires_in\":300}")
            );

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
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
        Handler.Respond = (_, _) => Task.FromResult(CreateSuccess("token-1", expiresIn));

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
        );

        Assert.Contains(OpenIdProtocol.Parameters.ExpiresIn, exception.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheFirstCallerCancels_TheSharedRequestCompletesForTheOthers()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var requestTokens = new List<CancellationToken>();
        Handler.Respond = (_, token) =>
        {
            requestTokens.Add(token);
            return pending.Task.WaitAsync(token);
        };
        using var firstCaller = new CancellationTokenSource();

        var first = TokenSource.GetAccessTokenAsync(firstCaller.Token).AsTask();
        var second = TokenSource.GetAccessTokenAsync(CancellationToken.None).AsTask();
        await firstCaller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(HangGuard));
        var requestToken = Assert.Single(requestTokens);
        Assert.False(requestToken.IsCancellationRequested);
        Assert.False(second.IsCompleted);

        pending.SetResult(CreateSuccess("token-1", 300));
        Assert.Equal("token-1", await second.WaitAsync(HangGuard));
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheRequestOutlastsItsTimeLimit_ThrowsDirectoryUnavailable()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Handler.Respond = (_, token) =>
        {
            token.Register(() => pending.TrySetCanceled(token));
            return pending.Task;
        };

        var call = TokenSource.GetAccessTokenAsync(CancellationToken.None).AsTask();
        TimeProvider.Advance(RequestTimeout);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => call.WaitAsync(HangGuard));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithinTheFailureWindow_ThrowsWithoutRequesting()
    {
        Handler.Respond = (_, _) =>
            Task.FromResult(Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}"));

        await Assert.ThrowsAsync<DirectoryClientNotRegisteredException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
        );
        TimeProvider.Advance(FailureCacheDuration - TimeSpan.FromMilliseconds(1));

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
        );

        Assert.Contains("failed moments ago", exception.Message);
        Assert.Single(Handler.Requests);
    }

    [Fact]
    public async Task GetAccessTokenAsync_AfterTheFailureWindow_RequestsAgain()
    {
        var requests = 0;
        Handler.Respond = (_, _) =>
            Task.FromResult(
                Interlocked.Increment(ref requests) == 1
                    ? Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}")
                    : CreateSuccess("token-2", 300)
            );

        await Assert.ThrowsAsync<DirectoryClientNotRegisteredException>(async () =>
            await TokenSource.GetAccessTokenAsync(CancellationToken.None)
        );
        TimeProvider.Advance(FailureCacheDuration);

        var token = await TokenSource.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", token);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheCallersTokenIsAlreadyCancelled_SendsNoRequest()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await TokenSource.GetAccessTokenAsync(cancellation.Token)
        );

        Assert.Empty(Handler.Requests);
    }

    #endregion

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        List<KeyValuePair<string, string>> Form
    );

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>
        > Respond { get; set; } =
            (_, _) => throw new InvalidOperationException("No response is set up.");

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var form = new List<KeyValuePair<string, string>>();
            if (request.Content is { } content)
            {
                var text = await content.ReadAsStringAsync(cancellationToken);
                foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split('=', 2);
                    var key = Uri.UnescapeDataString(parts[0]);
                    var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                    form.Add(new KeyValuePair<string, string>(key, value));
                }
            }

            Requests.Add(
                new RecordedRequest(request.Method, request.RequestUri!.AbsoluteUri, form)
            );
            return await Respond(request, cancellationToken);
        }
    }

    private sealed class FakeAuthenticator : IClientAuthenticator
    {
        public List<ClientAuthenticationContext> Contexts { get; } = [];

        public string MethodName => OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt;

        public bool UsesMutualTls => false;

        public ValueTask AuthenticateAsync(
            ClientAuthenticationContext context,
            CancellationToken cancellationToken
        )
        {
            Contexts.Add(context);
            context.Parameters.Add(
                new KeyValuePair<string, string>(
                    OpenIdProtocol.Parameters.ClientAssertion,
                    "assertion"
                )
            );
            return ValueTask.CompletedTask;
        }
    }
}
