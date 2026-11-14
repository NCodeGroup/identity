// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class RemotePlatformDirectoryTests : RemoteDirectoryTestBase
{
    private const string PlatformDocumentUrl =
        "https://directory.example.test/directory/v1/platform/document";

    private static JsonSerializerOptions WireOptions { get; } = new(JsonSerializerDefaults.Web);

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenCalled_PutsTheDocument()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NoContent);
        var directory = new RemotePlatformDirectory(CreateClient(handler));
        var document = new PlatformDocument { SettingsJson = """{"max_tenants":5}""" };

        await directory.PublishAsync(document, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(PlatformDocumentUrl, request.RequestUri?.AbsoluteUri);
        Assert.Equal($"Bearer {AccessToken}", request.Authorization);
        Assert.NotNull(request.Body);
        Assert.Equal(
            document,
            JsonSerializer.Deserialize<PlatformDocument>(request.Body, WireOptions)
        );
    }

    [Fact]
    public async Task PublishAsync_WhenDirectoryAnswersServiceUnavailable_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = new RemotePlatformDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.PublishAsync(new PlatformDocument(), CancellationToken.None)
        );
    }

    [Fact]
    public async Task PublishAsync_WhenTheConnectionIsRefused_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Throw(new HttpRequestException("Connection refused."));
        var directory = new RemotePlatformDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.PublishAsync(new PlatformDocument(), CancellationToken.None)
        );
    }

    #endregion

    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_WhenDirectoryAnswersTheDocument_ReturnsIt()
    {
        var expected = new PlatformDocument
        {
            SettingsJson = """{"max_tenants":5}""",
            Sequence = 7,
        };
        var handler = StubHttpMessageHandler.Respond(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(expected, WireOptions)
        );
        var directory = new RemotePlatformDirectory(CreateClient(handler));

        var result = await directory.GetOrDefaultAsync(CancellationToken.None);

        Assert.Equal(expected, result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(PlatformDocumentUrl, request.RequestUri?.AbsoluteUri);
        Assert.Equal($"Bearer {AccessToken}", request.Authorization);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenDirectoryAnswersNotFound_ReturnsNull()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NotFound);
        var directory = new RemotePlatformDirectory(CreateClient(handler));

        var result = await directory.GetOrDefaultAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenDirectoryAnswersServiceUnavailable_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = new RemotePlatformDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.GetOrDefaultAsync(CancellationToken.None)
        );
    }

    #endregion
}
