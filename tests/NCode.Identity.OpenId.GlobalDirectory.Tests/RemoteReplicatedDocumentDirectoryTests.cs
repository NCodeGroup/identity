// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class RemoteReplicatedDocumentDirectoryTests : RemoteDirectoryTestBase
{
    private const string Origin = "https://directory.example.test/directory/v1/";

    private static JsonSerializerOptions WireOptions { get; } = new(JsonSerializerDefaults.Web);

    private static ReplicatedDocument CreateDocument(string kind, string key = "") =>
        new()
        {
            Kind = kind,
            Key = key,
            Revision = 3,
            Content = JsonElement.Parse("""{"assignments":[],"clients":[]}"""),
        };

    #region PublishAsync Tests

    [Theory]
    [InlineData(ReplicatedDocumentKinds.PlatformAuthority, "", "platform/authority")]
    [InlineData(ReplicatedDocumentKinds.ManagementKeys, "", "platform/management-keys")]
    [InlineData(ReplicatedDocumentKinds.TeamAuthority, "team-a", "teams/team-a/authority")]
    public async Task PublishAsync_WhenCalled_PutsTheDocumentAndReturnsTheStoredRevision(
        string kind,
        string key,
        string path
    )
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, """{"revision":9}""");
        var directory = new RemoteReplicatedDocumentDirectory(CreateClient(handler));
        var document = CreateDocument(kind, key);

        var revision = await directory.PublishAsync(document, CancellationToken.None);

        Assert.Equal(9, revision);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal($"{Origin}{path}", request.RequestUri?.AbsoluteUri);
        Assert.Equal($"Bearer {AccessToken}", request.Authorization);
        Assert.NotNull(request.Body);
        var sent = JsonSerializer.Deserialize<ReplicatedDocument>(request.Body, WireOptions);
        Assert.NotNull(sent);
        Assert.Equal(kind, sent.Kind);
        Assert.Equal(key, sent.Key);
        Assert.Equal(document.Revision, sent.Revision);
    }

    [Fact]
    public async Task PublishAsync_WhenDirectoryAnswersServiceUnavailable_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = new RemoteReplicatedDocumentDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.PublishAsync(
                CreateDocument(ReplicatedDocumentKinds.PlatformAuthority),
                CancellationToken.None
            )
        );
    }

    #endregion

    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_WhenDirectoryAnswersTheDocument_ReturnsIt()
    {
        var expected = CreateDocument(ReplicatedDocumentKinds.TeamAuthority, "team-a");
        var handler = StubHttpMessageHandler.Respond(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(expected, WireOptions)
        );
        var directory = new RemoteReplicatedDocumentDirectory(CreateClient(handler));

        var result = await directory.GetOrDefaultAsync(
            ReplicatedDocumentKinds.TeamAuthority,
            "team-a",
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(expected.Revision, result.Revision);
        Assert.Equal("team-a", result.Key);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{Origin}teams/team-a/authority", request.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenDirectoryAnswersNotFound_ReturnsNull()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NotFound);
        var directory = new RemoteReplicatedDocumentDirectory(CreateClient(handler));

        var result = await directory.GetOrDefaultAsync(
            ReplicatedDocumentKinds.ManagementKeys,
            "",
            CancellationToken.None
        );

        Assert.Null(result);
    }

    #endregion

    #region ListRevisionsAsync Tests

    [Fact]
    public async Task ListRevisionsAsync_WhenCalled_ReturnsTheRevisions()
    {
        var body = """[{"kind":"platform-authority","key":"","revision":4}]""";
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, body);
        var directory = new RemoteReplicatedDocumentDirectory(CreateClient(handler));

        var result = await directory.ListRevisionsAsync(CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(ReplicatedDocumentKinds.PlatformAuthority, item.Kind);
        Assert.Equal(4, item.Revision);
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{Origin}replicated-revisions", request.RequestUri?.AbsoluteUri);
    }

    #endregion
}
