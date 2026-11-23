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

    private static JsonSerializerOptions WireOptions { get; } = new(JsonSerializerDefaults.Web);

    private RemoteControlPlanePrincipalDirectory CreateDirectory(
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

        return new RemoteControlPlanePrincipalDirectory(
            client,
            Options.Create(
                new CellOptions { ControlPlane = new ControlPlaneOptions { Issuer = issuer } }
            )
        );
    }

    #region FindAsync Tests

    [Fact]
    public async Task FindAsync_WhenTheQueryIsEmpty_ReturnsNothingWithoutCallingTheControlPlane()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, Answer);
        var directory = CreateDirectory(handler);

        var result = await directory.FindAsync(
            new ControlPlanePrincipalQuery(),
            CancellationToken.None
        );

        Assert.Empty(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FindAsync_WhenTheQueryIsSmall_PostsOneRequestWithTheControlPlaneToken()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, Answer);
        var directory = CreateDirectory(handler);

        var result = await directory.FindAsync(
            new ControlPlanePrincipalQuery { PrincipalIds = ["p1"], UserNames = ["ada"] },
            CancellationToken.None
        );

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
    public async Task FindAsync_WhenTheQueryNamesMoreThanTheMaximum_SplitsItAndConcatenatesTheAnswers()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, Answer);
        var directory = CreateDirectory(handler);
        var ids = Enumerable
            .Range(0, ControlPlanePrincipalQuery.MaxNames + 1)
            .Select(index => $"p{index}")
            .ToList();

        var result = await directory.FindAsync(
            new ControlPlanePrincipalQuery { PrincipalIds = ids },
            CancellationToken.None
        );

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task FindAsync_WhenTheControlPlaneFails_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = CreateDirectory(handler);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.FindAsync(
                new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] },
                CancellationToken.None
            )
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

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.FindAsync(
                new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] },
                CancellationToken.None
            )
        );

        Assert.Empty(handler.Requests);
    }

    #endregion
}
