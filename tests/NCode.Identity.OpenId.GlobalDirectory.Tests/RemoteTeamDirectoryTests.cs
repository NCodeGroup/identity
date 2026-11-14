// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Web;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class RemoteTeamDirectoryTests : RemoteDirectoryTestBase
{
    private static JsonSerializerOptions WireOptions { get; } = new(JsonSerializerDefaults.Web);

    private static string ToJson(IReadOnlyList<DirectoryChange> changes) =>
        JsonSerializer.Serialize(changes, WireOptions);

    private static StubHttpMessageHandler CreatePagedHandler(
        IReadOnlyList<DirectoryChange> changes
    ) =>
        new(
            (request, _) =>
            {
                var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
                var after = long.Parse(query["after"]!, CultureInfo.InvariantCulture);
                var limit = int.Parse(query["limit"]!, CultureInfo.InvariantCulture);
                var page = changes.Where(x => x.Sequence > after).Take(limit).ToList();
                return Task.FromResult(
                    StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, ToJson(page))
                );
            }
        );

    #region ListChangesAsync Tests

    [Fact]
    public async Task ListChangesAsync_WhenCalled_SendsAfterAndLimitAsTheQuery()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, "[]");
        var directory = new RemoteTeamDirectory(CreateClient(handler));

        var result = await directory.ListChangesAsync(41, 25, CancellationToken.None);

        Assert.Empty(result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://directory.example.test/directory/v1/teams/changes?after=41&limit=25",
            request.RequestUri?.AbsoluteUri
        );
        Assert.Equal($"Bearer {AccessToken}", request.Authorization);
    }

    [Fact]
    public async Task ListChangesAsync_WhenRequestedAfterThePreviousPage_PagesByAfter()
    {
        var changes = new List<DirectoryChange>
        {
            new() { Sequence = 1, TeamId = "team-1" },
            new() { Sequence = 2, TeamId = "team-2" },
            new() { Sequence = 3, TeamId = "team-3" },
        };
        var handler = CreatePagedHandler(changes);
        var directory = new RemoteTeamDirectory(CreateClient(handler));

        var first = await directory.ListChangesAsync(0, 2, CancellationToken.None);
        var second = await directory.ListChangesAsync(
            first[^1].Sequence,
            2,
            CancellationToken.None
        );

        Assert.Equal([1L, 2L], first.Select(x => x.Sequence));
        Assert.Equal([3L], second.Select(x => x.Sequence));
        Assert.Equal(
            ["?after=0&limit=2", "?after=2&limit=2"],
            handler.Requests.Select(x => x.RequestUri!.Query)
        );
    }

    [Fact]
    public async Task ListChangesAsync_WhenDirectoryAnswersServiceUnavailable_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.ServiceUnavailable);
        var directory = new RemoteTeamDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.ListChangesAsync(0, 10, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ListChangesAsync_WhenTheBodyIsNotAList_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, "{ not a list");
        var directory = new RemoteTeamDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.ListChangesAsync(0, 10, CancellationToken.None)
        );
    }

    #endregion

    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_WhenDirectoryAnswersConflict_ReturnsConflict()
    {
        var handler = StubHttpMessageHandler.Respond(
            HttpStatusCode.Conflict,
            """{"detail":"The team is reserved."}"""
        );
        var directory = new RemoteTeamDirectory(CreateClient(handler));

        var result = await directory.ReserveAsync("team-1", CancellationToken.None);

        Assert.False(result.IsReserved);
        Assert.Equal("The team is reserved.", result.ConflictDetail);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://directory.example.test/directory/v1/teams/team-1/reserve",
            request.RequestUri?.AbsoluteUri
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheConnectionIsRefused_ThrowsDirectoryUnavailable()
    {
        var handler = StubHttpMessageHandler.Throw(new HttpRequestException("Connection refused."));
        var directory = new RemoteTeamDirectory(CreateClient(handler));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.ReserveAsync("team-1", CancellationToken.None)
        );
    }

    #endregion
}
