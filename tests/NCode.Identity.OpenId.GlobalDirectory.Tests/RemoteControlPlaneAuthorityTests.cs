// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.Management.ControlPlane;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class RemoteControlPlaneAuthorityTests : BaseTests
{
    private const string AccessToken = "control-plane-access-token";
    private const string DirectoryUrl = "https://directory.example.test/";
    private const string Issuer = "https://auth.example.test/some/path";
    private const string AuthorityUrl = "https://auth.example.test/control-plane/v1/authority";

    private static JsonSerializerOptions WireOptions { get; } = new(JsonSerializerDefaults.Web);

    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static ControlPlaneAuthorityRequest CreateRequest(
        string subject = "subject-1",
        string clientId = "client-1",
        string? teamId = "team-1"
    ) =>
        new()
        {
            Subject = subject,
            ClientId = clientId,
            TeamId = teamId,
        };

    private static string CreateAnswer(params string[] teamRoles) =>
        JsonSerializer.Serialize(
            new ControlPlaneAuthority
            {
                PlatformRoles = ["GlobalAdmin"],
                TeamRoles = teamRoles,
                IsGlobalAdmin = true,
            },
            WireOptions
        );

    private RemoteControlPlaneAuthority CreateAuthority(
        HttpMessageHandler handler,
        string? issuer = Issuer
    )
    {
        var httpClient = new HttpClient(handler);

        var factoryMock = CreateStrictMock<IHttpClientFactory>();
        factoryMock
            .Setup(x => x.CreateClient(OpenIdClientDefaults.HttpClientName))
            .Returns(httpClient)
            .Verifiable(Times.Between(0, int.MaxValue, Moq.Range.Inclusive));

        var tokenSourceMock = CreateStrictMock<ICellTokenSource>();
        tokenSourceMock
            .Setup(x =>
                x.GetAccessTokenAsync(
                    GlobalDirectoryProtocol.ControlPlaneAudience,
                    GlobalDirectoryProtocol.ControlPlaneScope,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<string>(AccessToken))
            .Verifiable(Times.Between(0, int.MaxValue, Moq.Range.Inclusive));

        var client = new DirectoryHttpClient(
            factoryMock.Object,
            tokenSourceMock.Object,
            Options.Create(new DirectoryClientOptions { Url = DirectoryUrl })
        );

        return new RemoteControlPlaneAuthority(
            client,
            Options.Create(
                new CellOptions { ControlPlane = new ControlPlaneOptions { Issuer = issuer } }
            ),
            TimeProvider
        );
    }

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_WhenTheControlPlaneAnswers_MapsTheAnswer()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, CreateAnswer("Owner"));
        var authority = CreateAuthority(handler);

        var result = await authority.GetAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(["GlobalAdmin"], result.PlatformRoles);
        Assert.Equal(["Owner"], result.TeamRoles);
        Assert.True(result.IsGlobalAdmin);
    }

    [Fact]
    public async Task GetAsync_WhenTheControlPlaneAnswersThatTheSubjectIsTheActingClient_CarriesTheFlag()
    {
        var handler = StubHttpMessageHandler.Respond(
            HttpStatusCode.OK,
            """{"principalId":"principal-1","isActingClient":true}"""
        );
        var authority = CreateAuthority(handler);

        var result = await authority.GetAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsActingClient);
        Assert.Equal("principal-1", result.PrincipalId);
    }

    [Fact]
    public async Task GetAsync_WhenCalled_PostsTheRequestToTheIssuersOriginWithTheControlPlaneToken()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, CreateAnswer());
        var authority = CreateAuthority(handler);

        await authority.GetAsync(CreateRequest(), CancellationToken.None);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal(AuthorityUrl, recorded.RequestUri?.AbsoluteUri);
        Assert.Equal($"Bearer {AccessToken}", recorded.Authorization);
        Assert.NotNull(recorded.Body);
        var body = JsonSerializer.Deserialize<ControlPlaneAuthorityRequest>(
            recorded.Body,
            WireOptions
        );
        Assert.NotNull(body);
        Assert.Equal("subject-1", body.Subject);
        Assert.Equal("client-1", body.ClientId);
        Assert.Equal("team-1", body.TeamId);
    }

    [Fact]
    public async Task GetAsync_WhenAskedAgainWithinThirtySeconds_ServesTheAnswerFromTheCache()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, CreateAnswer("Owner"));
        var authority = CreateAuthority(handler);

        var first = await authority.GetAsync(CreateRequest(), CancellationToken.None);
        TimeProvider.Advance(TimeSpan.FromSeconds(29));
        var second = await authority.GetAsync(CreateRequest(), CancellationToken.None);

        Assert.Same(first, second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetAsync_WhenAskedAgainAfterThirtySeconds_CallsTheControlPlaneAgain()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, CreateAnswer("Owner"));
        var authority = CreateAuthority(handler);

        await authority.GetAsync(CreateRequest(), CancellationToken.None);
        TimeProvider.Advance(TimeSpan.FromSeconds(30));
        await authority.GetAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAsync_WhenAskedAboutAnotherCaller_DoesNotShareTheCachedAnswer()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, CreateAnswer("Owner"));
        var authority = CreateAuthority(handler);

        await authority.GetAsync(CreateRequest(), CancellationToken.None);
        await authority.GetAsync(CreateRequest(teamId: null), CancellationToken.None);
        await authority.GetAsync(CreateRequest(subject: "subject-2"), CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAsync_WhenTheCacheIsFull_DropsTheExpiredEntriesBeforeAddingAnother()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, CreateAnswer());
        var authority = CreateAuthority(handler);

        for (var index = 0; index < 1024; index++)
        {
            await authority.GetAsync(CreateRequest($"subject-{index}"), CancellationToken.None);
        }

        TimeProvider.Advance(TimeSpan.FromSeconds(31));
        await authority.GetAsync(CreateRequest("subject-new"), CancellationToken.None);
        var before = handler.Requests.Count;
        await authority.GetAsync(CreateRequest("subject-new"), CancellationToken.None);
        await authority.GetAsync(CreateRequest("subject-0"), CancellationToken.None);

        Assert.Equal(before + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAsync_WhenTheCacheIsFullOfFreshEntries_ClearsItBeforeAddingAnother()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, CreateAnswer());
        var authority = CreateAuthority(handler);

        for (var index = 0; index < 1024; index++)
        {
            await authority.GetAsync(CreateRequest($"subject-{index}"), CancellationToken.None);
        }

        await authority.GetAsync(CreateRequest("subject-new"), CancellationToken.None);
        var before = handler.Requests.Count;
        await authority.GetAsync(CreateRequest("subject-new"), CancellationToken.None);
        await authority.GetAsync(CreateRequest("subject-0"), CancellationToken.None);

        Assert.Equal(before + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAsync_WhenTheControlPlaneIsUnavailable_ThrowsAndDoesNotCacheTheFailure()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(
            (_, _) =>
                Task.FromResult(
                    ++calls == 1
                        ? StubHttpMessageHandler.CreateResponse(
                            HttpStatusCode.ServiceUnavailable,
                            ""
                        )
                        : StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, CreateAnswer())
                )
        );
        var authority = CreateAuthority(handler);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await authority.GetAsync(CreateRequest(), CancellationToken.None)
        );
        var result = await authority.GetAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsGlobalAdmin);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAsync_WhenTheRequestTimesOut_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Throw(new TaskCanceledException("Timed out."));
        var authority = CreateAuthority(handler);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await authority.GetAsync(CreateRequest(), CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetAsync_WhenTheControlPlaneAnswersForbidden_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.Forbidden);
        var authority = CreateAuthority(handler);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await authority.GetAsync(CreateRequest(), CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetAsync_WhenTheControlPlaneAnswersNotFound_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NotFound);
        var authority = CreateAuthority(handler);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await authority.GetAsync(CreateRequest(), CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetAsync_WhenTheBodyIsNull_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, "null");
        var authority = CreateAuthority(handler);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await authority.GetAsync(CreateRequest(), CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetAsync_WhenTheBodyIsNotJson_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, "not json");
        var authority = CreateAuthority(handler);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await authority.GetAsync(CreateRequest(), CancellationToken.None)
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a uri")]
    public async Task GetAsync_WhenTheIssuerIsMissingOrInvalid_ThrowsDirectoryUnavailableWithoutCallingTheControlPlane(
        string? issuer
    )
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, CreateAnswer());
        var authority = CreateAuthority(handler, issuer);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await authority.GetAsync(CreateRequest(), CancellationToken.None)
        );

        Assert.Empty(handler.Requests);
    }

    #endregion
}
