// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenTheDocumentHasAParent_SendsTheParentTeamIdInTheBody()
    {
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.NoContent);
        var directory = new RemoteTeamDirectory(CreateClient(handler));
        var document = new TeamDocument
        {
            TeamId = "team-2",
            DisplayName = "Team 2",
            IsDisabled = false,
            ParentTeamId = "team-1",
            Settings = JsonElement.Parse("{}"),
            RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
        };

        await directory.PublishAsync(document, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.NotNull(request.Body);
        var sent = JsonSerializer.Deserialize<TeamDocument>(request.Body, WireOptions);
        Assert.NotNull(sent);
        Assert.Equal("team-1", sent.ParentTeamId);
    }

    #endregion

    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheDocumentHasAParent_ReadsTheParentTeamId()
    {
        var document = new TeamDocument
        {
            TeamId = "team-2",
            DisplayName = "Team 2",
            IsDisabled = false,
            ParentTeamId = "team-1",
            Settings = JsonElement.Parse("{}"),
            RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
        };
        var handler = StubHttpMessageHandler.Respond(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(document, WireOptions)
        );
        var directory = new RemoteTeamDirectory(CreateClient(handler));

        var result = await directory.GetOrDefaultAsync("team-2", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("team-1", result.ParentTeamId);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("rootKeys")]
    public async Task GetOrDefaultAsync_WhenTheDocumentLacksItsSettingsOrRootKeys_ThrowsDirectoryUnavailable(
        string memberName
    )
    {
        var document = Assert.IsType<JsonObject>(
            JsonSerializer.SerializeToNode(
                new TeamDocument
                {
                    TeamId = "team-2",
                    DisplayName = "Team 2",
                    IsDisabled = false,
                    Settings = JsonElement.Parse("{}"),
                    RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
                },
                WireOptions
            )
        );
        document.Remove(memberName);
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, document.ToJsonString());
        var directory = new RemoteTeamDirectory(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.GetOrDefaultAsync("team-2", CancellationToken.None)
        );

        Assert.IsType<JsonException>(exception.InnerException);
    }

    #endregion

    #region ListChangesAsync Tests

    [Fact]
    public async Task ListChangesAsync_WhenTheFeedHoldsAPlatformChange_ReadsThePlatformDocumentWithoutATeamId()
    {
        var changes = new List<DirectoryChange>
        {
            new()
            {
                Sequence = 1,
                TeamId = "team-1",
                Document = new TeamDocument
                {
                    TeamId = "team-1",
                    DisplayName = "Team 1",
                    IsDisabled = false,
                    Settings = JsonElement.Parse("{}"),
                    RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
                },
            },
            new()
            {
                Sequence = 2,
                Platform = new PlatformDocument
                {
                    Settings = JsonElement.Parse("""{"max_tenants":5}"""),
                    Sequence = 2,
                },
            },
        };
        var handler = CreatePagedHandler(changes);
        var directory = new RemoteTeamDirectory(CreateClient(handler));

        var result = await directory.ListChangesAsync(0, 10, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("team-1", result[0].TeamId);
        Assert.Null(result[0].Platform);
        Assert.Null(result[1].TeamId);
        Assert.Null(result[1].Document);
        var expectedPlatform = changes[1].Platform;
        var platform = result[1].Platform;
        Assert.NotNull(expectedPlatform);
        Assert.NotNull(platform);
        Assert.True(JsonElement.DeepEquals(expectedPlatform.Settings, platform.Settings));
        Assert.Equal(expectedPlatform.Sequence, platform.Sequence);
    }

    #endregion

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

    [Fact]
    public async Task ListChangesAsync_WhenAChangedDocumentLacksItsRootKeys_ThrowsDirectoryUnavailable()
    {
        var page = Assert.IsType<JsonArray>(
            JsonSerializer.SerializeToNode(
                new List<DirectoryChange>
                {
                    new()
                    {
                        Sequence = 1,
                        TeamId = "team-1",
                        Document = new TeamDocument
                        {
                            TeamId = "team-1",
                            DisplayName = "Team 1",
                            IsDisabled = false,
                            Settings = JsonElement.Parse("{}"),
                            RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
                        },
                    },
                },
                WireOptions
            )
        );
        var change = Assert.IsType<JsonObject>(page[0]);
        Assert.IsType<JsonObject>(change["document"]).Remove("rootKeys");
        var handler = StubHttpMessageHandler.Respond(HttpStatusCode.OK, page.ToJsonString());
        var directory = new RemoteTeamDirectory(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await directory.ListChangesAsync(0, 10, CancellationToken.None)
        );

        Assert.IsType<JsonException>(exception.InnerException);
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
