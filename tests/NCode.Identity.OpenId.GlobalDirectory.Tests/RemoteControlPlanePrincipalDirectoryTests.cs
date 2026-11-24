// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.GlobalDirectory.ControlPlane;
using NCode.Identity.OpenId.Management.ControlPlane;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class RemoteControlPlanePrincipalDirectoryTests : BaseTests
{
    private const string AccessToken = "control-plane-access-token";
    private const string DirectoryUrl = "https://directory.example.test/";
    private const string Issuer = "https://auth.example.test/some/path";
    private const string PrincipalsUrl = "https://auth.example.test/control-plane/v1/principals";
    private const string Answer =
        """{"items":[{"principalId":"p1","principalType":"User","displayName":"Ada","userName":"ada"}]}""";

    private const string OtherAnswer =
        """{"items":[{"principalId":"p100","principalType":"User","displayName":"Bob","userName":"bob"}]}""";
    private const string AgainAnswer =
        """{"items":[{"principalId":"P1","principalType":"User","displayName":"Ada (again)","userName":"ada"}]}""";

    private static JsonSerializerOptions WireOptions { get; } = new(JsonSerializerDefaults.Web);

    private static ControlPlanePrincipalQuery DeserializeBody(string? body)
    {
        Assert.NotNull(body);
        var query = JsonSerializer.Deserialize<ControlPlanePrincipalQuery>(body, WireOptions);
        Assert.NotNull(query);
        return query;
    }

    private static StubHttpMessageHandler RespondInTurn(IReadOnlyList<string> answers)
    {
        var next = 0;
        return new StubHttpMessageHandler(
            (_, _) =>
            {
                var answer = answers[next];
                next++;
                var response = StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, answer);
                return Task.FromResult(response);
            }
        );
    }

    private RemoteControlPlanePrincipalDirectory CreateDirectory(
        HttpMessageHandler handler,
        string? issuer = Issuer
    )
    {
        var httpClient = new HttpClient(handler);
        var anyTimes = Times.Between(0, int.MaxValue, Moq.Range.Inclusive);

        var factoryMock = CreateStrictMock<IHttpClientFactory>();
        factoryMock
            .Setup(x => x.CreateClient(OpenIdClientDefaults.HttpClientName))
            .Returns(httpClient)
            .Verifiable(anyTimes);

        var tokenSourceMock = CreateStrictMock<ICellTokenSource>();
        tokenSourceMock
            .Setup(x =>
                x.GetAccessTokenAsync(
                    GlobalDirectoryProtocol.ControlPlaneAudience,
                    GlobalDirectoryProtocol.ControlPlaneScope,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(AccessToken)
            .Verifiable(anyTimes);

        var directoryClientOptions = new DirectoryClientOptions { Url = DirectoryUrl };
        var clientOptions = Options.Create(directoryClientOptions);
        var client = new DirectoryHttpClient(
            factoryMock.Object,
            tokenSourceMock.Object,
            clientOptions
        );

        var cell = new CellOptions { ControlPlane = new ControlPlaneOptions { Issuer = issuer } };
        var cellOptions = Options.Create(cell);
        return new RemoteControlPlanePrincipalDirectory(client, cellOptions);
    }

    #region FindAsync Tests

    [Fact]
    public async Task FindAsync_WhenTheQueryIsEmpty_ReturnsNothingWithoutCallingTheControlPlane()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, Answer);
        var directory = CreateDirectory(handler);

        var query = new ControlPlanePrincipalQuery();
        var result = await directory.FindAsync(query, CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FindAsync_WhenTheQueryIsSmall_PostsOneRequestWithTheControlPlaneToken()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, Answer);
        var directory = CreateDirectory(handler);

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"], UserNames = ["ada"] };
        var result = await directory.FindAsync(query, CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal("p1", summary.PrincipalId);
        Assert.Equal("Ada", summary.DisplayName);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal(PrincipalsUrl, recorded.RequestUri?.AbsoluteUri);
        Assert.Equal($"Bearer {AccessToken}", recorded.Authorization);
        Assert.NotNull(recorded.Body);
        var body = JsonSerializer.Deserialize<ControlPlanePrincipalQuery>(
            recorded.Body,
            WireOptions
        );
        Assert.NotNull(body);
        Assert.Equal(["p1"], body.PrincipalIds);
        Assert.Equal(["ada"], body.UserNames);
    }

    [Fact]
    public async Task FindAsync_WhenTheQueryNamesMoreThanTheMaximum_SplitsItAndJoinsTheAnswers()
    {
        var handler = RespondInTurn([Answer, OtherAnswer]);
        var directory = CreateDirectory(handler);
        var ids = Enumerable
            .Range(0, ControlPlanePrincipalQuery.MaxNames + 1)
            .Select(index => $"p{index}")
            .ToList();

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ids };
        var result = await directory.FindAsync(query, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var principalIds = result.Select(summary => summary.PrincipalId).ToList();
        Assert.Equal(["p1", "p100"], principalIds);
        var first = DeserializeBody(handler.Requests[0].Body);
        var second = DeserializeBody(handler.Requests[1].Body);
        var firstIds = ids.GetRange(0, ControlPlanePrincipalQuery.MaxNames);
        Assert.Equal(firstIds, first.PrincipalIds);
        Assert.Empty(first.UserNames);
        Assert.Equal([ids[ControlPlanePrincipalQuery.MaxNames]], second.PrincipalIds);
    }

    [Fact]
    public async Task FindAsync_WhenTwoChunksNameOnePrincipal_ReturnsItsFirstSummaryOnce()
    {
        var handler = RespondInTurn([Answer, AgainAnswer]);
        var directory = CreateDirectory(handler);
        var ids = Enumerable
            .Range(0, ControlPlanePrincipalQuery.MaxNames)
            .Select(index => $"p{index}")
            .ToList();

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ids, UserNames = ["ada"] };
        var result = await directory.FindAsync(query, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var summary = Assert.Single(result);
        Assert.Equal("p1", summary.PrincipalId);
        Assert.Equal("Ada", summary.DisplayName);
    }

    [Fact]
    public async Task FindAsync_WhenTheQueryNamesExactlyTheMaximum_PostsOneRequest()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, Answer);
        var directory = CreateDirectory(handler);
        var ids = Enumerable
            .Range(0, ControlPlanePrincipalQuery.MaxNames)
            .Select(index => $"p{index}")
            .ToList();

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ids };
        await directory.FindAsync(query, CancellationToken.None);

        var recorded = Assert.Single(handler.Requests);
        var body = DeserializeBody(recorded.Body);
        Assert.Equal(ids, body.PrincipalIds);
        Assert.Empty(body.UserNames);
    }

    [Fact]
    public async Task FindAsync_WhenTheIdsAndUserNamesCrossTheMaximum_SplitsThemInOrder()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, Answer);
        var directory = CreateDirectory(handler);
        var ids = Enumerable
            .Range(0, ControlPlanePrincipalQuery.MaxNames)
            .Select(index => $"p{index}")
            .ToList();

        var query = new ControlPlanePrincipalQuery
        {
            PrincipalIds = ids,
            UserNames = ["ada", "bob"],
        };
        await directory.FindAsync(query, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var first = DeserializeBody(handler.Requests[0].Body);
        var second = DeserializeBody(handler.Requests[1].Body);
        Assert.Equal(ids, first.PrincipalIds);
        Assert.Empty(first.UserNames);
        Assert.Empty(second.PrincipalIds);
        Assert.Equal(["ada", "bob"], second.UserNames);
    }

    [Fact]
    public async Task FindAsync_WhenTheControlPlaneFails_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = CreateDirectory(handler);

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };
        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.FindAsync(query, CancellationToken.None)
        );

        Assert.Equal($"{PrincipalsUrl} answered 503 to POST.", exception.Message);
    }

    [Fact]
    public async Task FindAsync_WhenTheControlPlaneAnswersNotFound_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NotFound);
        var directory = CreateDirectory(handler);

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };
        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.FindAsync(query, CancellationToken.None)
        );

        Assert.Equal(
            "The directory answered 404 to describe control-plane principals.",
            exception.Message
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a uri")]
    public async Task FindAsync_WhenTheIssuerIsMissingOrInvalid_ThrowsDirectoryUnavailableWithoutCallingTheControlPlane(
        string? issuer
    )
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, Answer);
        var directory = CreateDirectory(handler, issuer);

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };
        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.FindAsync(query, CancellationToken.None)
        );

        Assert.Equal("The cell does not know the issuer of the control plane.", exception.Message);
        Assert.Empty(handler.Requests);
    }

    #endregion
}
