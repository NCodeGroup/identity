// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryHttpClientTests : RemoteDirectoryTestBase
{
    #region SendAsync Tests

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task SendAsync_WhenStatusIsSuccessNotFoundOrConflict_ReturnsTheResponse(
        HttpStatusCode statusCode
    )
    {
        var handler = StubHttpMessageHandler.Respond(statusCode);
        var client = CreateClient(handler);

        var response = await client.SendAsync(
            HttpMethod.Get,
            "tenants/tenant-1",
            body: null,
            CancellationToken.None
        );

        Assert.Equal(statusCode, response.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task SendAsync_WhenStatusIsAnyOtherFailure_ThrowsDirectoryUnavailable(
        HttpStatusCode statusCode
    )
    {
        var handler = StubHttpMessageHandler.Respond(statusCode);
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await client.SendAsync(
                HttpMethod.Get,
                "tenants/tenant-1",
                body: null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task SendAsync_WhenTheBodyExceedsTheLimit_ThrowsDirectoryUnavailable()
    {
        var body = new string('x', DirectoryClientDefaults.MaxResponseBytes + 1);
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, body);
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await client.SendAsync(
                HttpMethod.Get,
                "tenants/tenant-1",
                body: null,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task SendAsync_WhenTheCallerCancels_PropagatesTheCancellation()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var handler = new StubHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                await cancellationTokenSource.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, "");
            }
        );
        var client = CreateClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.SendAsync(
                HttpMethod.Get,
                "tenants/tenant-1",
                body: null,
                cancellationTokenSource.Token
            )
        );
    }

    [Fact]
    public async Task SendAsync_WhenTheTokenSourceCannotObtainAToken_ThrowsDirectoryUnavailableWithoutCallingTheDirectory()
    {
        var factoryMock = CreateStrictMock<IHttpClientFactory>();
        var expected = new DirectoryUnavailableException("No token.");
        var tokenSourceMock = CreateStrictMock<ICellTokenSource>();
        tokenSourceMock
            .Setup(x =>
                x.GetAccessTokenAsync(
                    DirectoryClientDefaults.Audience,
                    DirectoryClientDefaults.Scope,
                    It.IsAny<CancellationToken>()
                )
            )
            .Throws(expected)
            .Verifiable(Times.Once);
        var client = new DirectoryHttpClient(
            factoryMock.Object,
            tokenSourceMock.Object,
            Options.Create(new DirectoryClientOptions { Url = BaseUrl })
        );

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await client.SendAsync(
                HttpMethod.Get,
                "tenants/tenant-1",
                body: null,
                CancellationToken.None
            )
        );

        Assert.Same(expected, exception);
    }

    #endregion
}
