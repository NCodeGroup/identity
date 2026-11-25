// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Identity.Secrets.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryReplicatedDocumentEndpointsTests : IAsyncLifetime
{
    private const string ServerIssuer = "https://control-plane.example";
    private const string Token = "directory-access-token";
    private const string CellName = "a";
    private const string ControlPlaneCell = "control";
    private const string TeamId = "team-1";

    private static string TeamPath => $"/directory/v1/teams/{TeamId}/authority";

    private static JsonSerializerOptions WebOptions { get; } = new(JsonSerializerDefaults.Web);

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    private SqliteConnection Connection { get; } = new("DataSource=:memory:");

    private WebApplication? App { get; set; }

    private HttpClient? ClientOrNull { get; set; }

    private HttpClient Client => ClientOrNull ?? throw new InvalidOperationException();

    public async Task InitializeAsync()
    {
        Connection.Open();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            DirectoryServerTestOptions.Create(ControlPlaneCell, "root", ServerIssuer)
        );
        builder.Services.AddDbContextFactory<DirectoryDbContext>(options =>
            options.UseSqlite(Connection)
        );
        var libraries = DirectoryServerTestOptions.AddDirectoryTokenServices(builder.Services);
        builder.Services.AddSingleton<
            IPublicJwksReader,
            DirectoryServerTestOptions.AcceptingPublicJwksReader
        >();
        libraries.AddOpenIdDirectoryServer(builder.Configuration);

        var app = builder.Build();
        app.MapDirectory();

        var dbContextFactory = app.Services.GetRequiredService<
            IDbContextFactory<DirectoryDbContext>
        >();
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync())
        {
            await dbContext.Database.EnsureCreatedAsync();
        }

        await app.StartAsync();

        App = app;
        ClientOrNull = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        ClientOrNull?.Dispose();
        if (App is not null)
            await App.DisposeAsync();

        await Connection.DisposeAsync();
        Mocks.Verify();
    }

    private async ValueTask AuthenticateAsync(
        string cell,
        CancellationToken cancellationToken = default
    )
    {
        var services = (App ?? throw new InvalidOperationException()).Services;
        var token = await DirectoryServerTestOptions.CreateTokenAsync(
            services,
            GlobalDirectoryProtocol.CellClientIdPrefix + cell,
            cancellationToken: cancellationToken
        );
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static ReplicatedDocument CreateDocument(
        string kind,
        string key,
        long revision,
        string contentJson = "{}"
    ) =>
        new()
        {
            Kind = kind,
            Key = key,
            Revision = revision,
            Content = JsonElement.Parse(contentJson),
        };

    private static async ValueTask<long> ReadRevisionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default
    )
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            WebOptions,
            cancellationToken
        );
        return body.GetProperty("revision").GetInt64();
    }

    [Fact]
    public async Task PutPlatformAuthority_FromTheControlPlane_StoresAndAnswersTheRevision()
    {
        await AuthenticateAsync(ControlPlaneCell);
        var document = CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 3);

        var response = await Client.PutAsJsonAsync(
            GlobalDirectoryProtocol.PlatformAuthorityPath,
            document,
            WebOptions
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, await ReadRevisionAsync(response));
    }

    [Fact]
    public async Task PutPlatformAuthority_WithALowerRevision_AnswersTheStoredRevision()
    {
        await AuthenticateAsync(ControlPlaneCell);
        var first = CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 5);
        var second = CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 2);
        await Client.PutAsJsonAsync(
            GlobalDirectoryProtocol.PlatformAuthorityPath,
            first,
            WebOptions
        );

        var response = await Client.PutAsJsonAsync(
            GlobalDirectoryProtocol.PlatformAuthorityPath,
            second,
            WebOptions
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, await ReadRevisionAsync(response));
    }

    [Fact]
    public async Task PutPlatformAuthority_FromAnotherCell_AnswersForbidden()
    {
        await AuthenticateAsync(CellName);
        var document = CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 1);

        var response = await Client.PutAsJsonAsync(
            GlobalDirectoryProtocol.PlatformAuthorityPath,
            document,
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutPlatformAuthority_WithAnotherKind_AnswersBadRequest()
    {
        await AuthenticateAsync(ControlPlaneCell);
        var document = CreateDocument(ReplicatedDocumentKinds.ManagementKeys, "", 1);

        var response = await Client.PutAsJsonAsync(
            GlobalDirectoryProtocol.PlatformAuthorityPath,
            document,
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutPlatformAuthority_WithOversizedContent_AnswersPayloadTooLarge()
    {
        await AuthenticateAsync(ControlPlaneCell);
        var big = JsonSerializer.Serialize(new string('x', DirectoryLimits.MaxDocumentBytes + 1));
        var document = CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 1, big);

        var response = await Client.PutAsJsonAsync(
            GlobalDirectoryProtocol.PlatformAuthorityPath,
            document,
            WebOptions
        );

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PutManagementKeys_ThenGet_ReturnsTheDocument()
    {
        await AuthenticateAsync(ControlPlaneCell);
        var document = CreateDocument(
            ReplicatedDocumentKinds.ManagementKeys,
            "",
            4,
            """{"keys":[],"revokedKeyIds":[]}"""
        );
        var put = await Client.PutAsJsonAsync(
            GlobalDirectoryProtocol.ManagementKeysPath,
            document,
            WebOptions
        );
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var response = await Client.GetAsync(GlobalDirectoryProtocol.ManagementKeysPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = await response.Content.ReadFromJsonAsync<ReplicatedDocument>(WebOptions);
        Assert.NotNull(actual);
        Assert.Equal(4, actual.Revision);
        Assert.Equal(ReplicatedDocumentKinds.ManagementKeys, actual.Kind);
    }

    [Fact]
    public async Task GetManagementKeys_WhenNonePublished_AnswersNotFound()
    {
        await AuthenticateAsync(ControlPlaneCell);

        var response = await Client.GetAsync(GlobalDirectoryProtocol.ManagementKeysPath);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPlatformAuthority_FromAnyCell_AnswersTheDocument()
    {
        await AuthenticateAsync(CellName);
        await StoreDirectlyAsync(CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 2));

        var response = await Client.GetAsync(GlobalDirectoryProtocol.PlatformAuthorityPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutTeamAuthority_WithTheRoutesKey_StoresIt()
    {
        await AuthenticateAsync(ControlPlaneCell);
        var document = CreateDocument(ReplicatedDocumentKinds.TeamAuthority, TeamId, 1);

        var response = await Client.PutAsJsonAsync(TeamPath, document, WebOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await ReadRevisionAsync(response));
    }

    [Fact]
    public async Task PutTeamAuthority_WithAnotherTeamsKey_AnswersBadRequest()
    {
        await AuthenticateAsync(ControlPlaneCell);
        var document = CreateDocument(ReplicatedDocumentKinds.TeamAuthority, "team-2", 1);

        var response = await Client.PutAsJsonAsync(TeamPath, document, WebOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutTeamAuthority_FromAnotherCell_AnswersForbidden()
    {
        await AuthenticateAsync(CellName);
        var document = CreateDocument(ReplicatedDocumentKinds.TeamAuthority, TeamId, 1);

        var response = await Client.PutAsJsonAsync(TeamPath, document, WebOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutManagementKeys_FromAnotherCell_AnswersForbidden()
    {
        await AuthenticateAsync(CellName);
        var document = CreateDocument(ReplicatedDocumentKinds.ManagementKeys, "", 1);

        var response = await Client.PutAsJsonAsync(
            GlobalDirectoryProtocol.ManagementKeysPath,
            document,
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutTeamAuthority_WithAnInvalidTeamIdFromAnotherCell_AnswersForbidden()
    {
        await AuthenticateAsync(CellName);
        var document = CreateDocument(ReplicatedDocumentKinds.TeamAuthority, "Bad_Team!", 1);

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/teams/Bad_Team!/authority",
            document,
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTeamAuthority_WhenNonePublished_AnswersNotFound()
    {
        await AuthenticateAsync(CellName);

        var response = await Client.GetAsync(TeamPath);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListRevisions_FromTheControlPlane_ListsRevisionsWithoutContent()
    {
        await AuthenticateAsync(ControlPlaneCell);
        await StoreDirectlyAsync(CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 7));

        var response = await Client.GetAsync(GlobalDirectoryProtocol.ReplicatedRevisionsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<ReplicatedRevisionPage>(WebOptions);
        Assert.NotNull(page);
        Assert.Null(page.Continuation);
        var revision = Assert.Single(page.Revisions);
        Assert.Equal(7, revision.Revision);
        Assert.Null(revision.Content);
    }

    [Fact]
    public async Task ListRevisions_WithALimit_PagesByKindAndKeyThroughTheContinuation()
    {
        await AuthenticateAsync(ControlPlaneCell);
        await StoreDirectlyAsync(CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 1));
        await StoreDirectlyAsync(CreateDocument(ReplicatedDocumentKinds.ManagementKeys, "", 2));
        await StoreDirectlyAsync(
            CreateDocument(ReplicatedDocumentKinds.TeamAuthority, "team-1", 3)
        );

        var path = $"{GlobalDirectoryProtocol.ReplicatedRevisionsPath}?limit=2";
        var first = await GetPageAsync(path);
        var second = await GetPageAsync(
            $"{path}&after={Uri.EscapeDataString(first.Continuation!)}"
        );

        Assert.Equal(2, first.Revisions.Count);
        Assert.Equal(ReplicatedDocumentKinds.ManagementKeys, first.Revisions[0].Kind);
        Assert.Equal(ReplicatedDocumentKinds.PlatformAuthority, first.Revisions[1].Kind);
        Assert.NotNull(first.Continuation);
        var last = Assert.Single(second.Revisions);
        Assert.Equal(ReplicatedDocumentKinds.TeamAuthority, last.Kind);
        Assert.Null(second.Continuation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ListRevisions_WithALimitBelowOne_ClampsToOne(int limit)
    {
        await AuthenticateAsync(ControlPlaneCell);
        await StoreDirectlyAsync(CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 1));
        await StoreDirectlyAsync(CreateDocument(ReplicatedDocumentKinds.ManagementKeys, "", 2));

        var page = await GetPageAsync(
            $"{GlobalDirectoryProtocol.ReplicatedRevisionsPath}?limit={limit}"
        );

        Assert.Single(page.Revisions);
        Assert.NotNull(page.Continuation);
    }

    [Fact]
    public async Task ListRevisions_WithALimitAboveTheMaximum_Answers200()
    {
        await AuthenticateAsync(ControlPlaneCell);
        await StoreDirectlyAsync(CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 1));

        var page = await GetPageAsync(
            $"{GlobalDirectoryProtocol.ReplicatedRevisionsPath}?limit=1000000"
        );

        Assert.Single(page.Revisions);
        Assert.Null(page.Continuation);
    }

    [Fact]
    public async Task ListRevisions_WhenTheLimitIsAboveTheMaximum_ReturnsAtMostTheMaximum()
    {
        await AuthenticateAsync(ControlPlaneCell);
        for (var index = 0; index < 1001; index++)
        {
            var key = $"team-{index:D4}";
            await StoreDirectlyAsync(CreateDocument(ReplicatedDocumentKinds.TeamAuthority, key, 1));
        }

        var page = await GetPageAsync(
            $"{GlobalDirectoryProtocol.ReplicatedRevisionsPath}?limit=1000000"
        );

        Assert.Equal(1000, page.Revisions.Count);
        Assert.NotNull(page.Continuation);
    }

    [Fact]
    public async Task ListRevisions_WithAnInvalidContinuation_AnswersBadRequest()
    {
        await AuthenticateAsync(ControlPlaneCell);

        var response = await Client.GetAsync(
            $"{GlobalDirectoryProtocol.ReplicatedRevisionsPath}?after=nocolon"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListRevisions_FromAnotherCell_AnswersForbidden()
    {
        await AuthenticateAsync(CellName);

        var response = await Client.GetAsync(GlobalDirectoryProtocol.ReplicatedRevisionsPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async ValueTask<ReplicatedRevisionPage> GetPageAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        var response = await Client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<ReplicatedRevisionPage>(
            WebOptions,
            cancellationToken
        );
        return Assert.IsType<ReplicatedRevisionPage>(page);
    }

    private async ValueTask StoreDirectlyAsync(
        ReplicatedDocument document,
        CancellationToken cancellationToken = default
    )
    {
        var directory = App!.Services.GetRequiredService<IReplicatedDocumentDirectory>();
        await directory.PublishAsync(document, cancellationToken);
    }
}
