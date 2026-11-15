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
using NCode.Identity.OpenId.Client;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryEndpointsTests : IAsyncLifetime
{
    private const string ServerIssuer = "https://control-plane.example";
    private const string Audience = "urn:ncode:directory";
    private const string Token = "directory-access-token";
    private const string CellClientId = "cell-a";
    private const string DirectoryScope = "directory";
    private const string TenantId = "tenant-1";
    private const string OtherTenantId = "tenant-2";
    private const string TenantIssuer = "https://op.example/tenant-1";
    private const string OtherTenantIssuer = "https://op.example/tenant-2";
    private const string TeamId = "team-1";

    private static JsonSerializerOptions WebOptions { get; } = new(JsonSerializerDefaults.Web);

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    private Mock<IIssuerAccessTokenValidator> ValidatorMock { get; }

    private SqliteConnection Connection { get; } = new("DataSource=:memory:");

    private WebApplication? App { get; set; }

    private HttpClient? ClientOrNull { get; set; }

    private HttpClient Client => ClientOrNull ?? throw new InvalidOperationException();

    public DirectoryEndpointsTests()
    {
        ValidatorMock = Mocks.Create<IIssuerAccessTokenValidator>();
    }

    public async Task InitializeAsync()
    {
        Connection.Open();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DirectoryServer:Issuer"] = ServerIssuer,
                ["AllowedHosts"] = "localhost",
            }
        );
        builder.Services.AddDbContextFactory<DirectoryDbContext>(options =>
            options.UseSqlite(Connection)
        );
        builder.Services.AddSingleton(ValidatorMock.Object);
        new ServiceBuilder<OpenIdClientLibrary>(builder.Services).AddOpenIdDirectoryServer(
            builder.Configuration
        );

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

    private static IssuerAccessTokenResult CreateValidResult(string clientId, string scope)
    {
        var payload = JsonElement.Parse(
            JsonSerializer.Serialize(new { client_id = clientId, scope })
        );
        return new IssuerAccessTokenResult(payload, ServerIssuer);
    }

    private void Authenticate(string clientId = CellClientId, string scope = DirectoryScope)
    {
        ValidatorMock
            .Setup(x =>
                x.ValidateAsync(ServerIssuer, Audience, Token, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateValidResult(clientId, scope))
            .Verifiable();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    private static async ValueTask<T> ReadAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default
    )
    {
        var value = await response.Content.ReadFromJsonAsync<T>(WebOptions, cancellationToken);
        return Assert.IsAssignableFrom<T>(value);
    }

    private static async ValueTask<string> ReadProblemDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default
    )
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await ReadAsync<JsonElement>(response, cancellationToken);
        return problem.GetProperty("detail").GetString() ?? string.Empty;
    }

    private async ValueTask<HttpResponseMessage> ReserveTenantAsync(
        string tenantId,
        string issuer,
        CancellationToken cancellationToken = default
    ) =>
        await Client.PostAsJsonAsync(
            "/directory/v1/tenants/reserve",
            new TenantReservation
            {
                TenantId = tenantId,
                Issuer = issuer,
                Cell = CellClientId,
            },
            WebOptions,
            cancellationToken
        );

    #region Authentication Tests

    [Fact]
    public async Task Request_WithoutAuthorization_AnswersUnauthorized()
    {
        var response = await Client.GetAsync("/directory/v1/cells");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Request_WithAnotherScheme_AnswersUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Token);

        var response = await Client.GetAsync("/directory/v1/cells");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithAnInvalidToken_AnswersUnauthorized()
    {
        ValidatorMock
            .Setup(x =>
                x.ValidateAsync(ServerIssuer, Audience, Token, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new IssuerAccessTokenResult(
                    TokenValidationFailureCodes.Expired,
                    "The token has expired."
                )
            )
            .Verifiable();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var response = await Client.GetAsync("/directory/v1/cells");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithATokenWithoutTheDirectoryScope_AnswersUnauthorized()
    {
        Authenticate(scope: "openid profile");

        var response = await Client.GetAsync("/directory/v1/cells");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithATokenWithoutAClientId_AnswersUnauthorized()
    {
        Authenticate(clientId: string.Empty);

        var response = await Client.GetAsync("/directory/v1/cells");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WhenTheIssuerIsUnavailable_AnswersServiceUnavailableWithRetryAfter()
    {
        ValidatorMock
            .Setup(x =>
                x.ValidateAsync(ServerIssuer, Audience, Token, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new IssuerAccessTokenResult(
                    TokenValidationFailureCodes.IssuerUnavailable,
                    "The issuer could not be reached."
                )
            )
            .Verifiable();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var response = await Client.GetAsync("/directory/v1/cells");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task Request_WithAScopeAmongOthers_IsAuthenticated()
    {
        Authenticate(scope: "openid directory control-plane");

        var response = await Client.GetAsync("/directory/v1/cells");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Tenant Tests

    [Fact]
    public async Task ReserveTenant_WhenFree_AnswersOk()
    {
        Authenticate();

        var response = await ReserveTenantAsync(TenantId, TenantIssuer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReserveTenant_WhenTheIdentifierIsHeld_AnswersConflictWithTheDetail()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var response = await ReserveTenantAsync(TenantId, OtherTenantIssuer);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"Another tenant already has the identifier '{TenantId}'.",
            await ReadProblemDetailAsync(response)
        );
    }

    [Fact]
    public async Task ReserveTenant_WhenTheIssuerIsHeld_AnswersConflictWithTheDetail()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var response = await ReserveTenantAsync(OtherTenantId, TenantIssuer);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"Another tenant already has the issuer '{TenantIssuer}'.",
            await ReadProblemDetailAsync(response)
        );
    }

    [Fact]
    public async Task ReserveTenant_WithoutAnIssuer_AnswersBadRequest()
    {
        Authenticate();

        var response = await ReserveTenantAsync(TenantId, string.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PlaceTenant_ThenGet_AnswersThePlacementInCamelCase()
    {
        Authenticate(clientId: $"cell-{CellClientId}");
        var cellResponse = await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{CellClientId}",
            CreateCell(CellClientId),
            WebOptions
        );
        Assert.Equal(HttpStatusCode.OK, cellResponse.StatusCode);
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var placeResponse = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/place",
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = TenantIssuer,
                Cell = CellClientId,
                Region = "region-a",
            },
            WebOptions
        );
        var getResponse = await Client.GetAsync($"/directory/v1/tenants/{TenantId}");

        Assert.Equal(HttpStatusCode.OK, placeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var json = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"tenantId\":\"tenant-1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"region\":\"region-a\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTenant_WhenNotPlaced_AnswersNotFound()
    {
        Authenticate();

        var response = await Client.GetAsync($"/directory/v1/tenants/{TenantId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseTenant_WhenReserved_FreesTheIdentifier()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var releaseResponse = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/release",
            content: null
        );
        var reserveResponse = await ReserveTenantAsync(TenantId, TenantIssuer);

        Assert.Equal(HttpStatusCode.OK, releaseResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reserveResponse.StatusCode);
    }

    [Fact]
    public async Task RetireTenant_ThenLookups_AnswerTheRetiredIdentifierAndIssuer()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var retireResponse = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/retire",
            new TenantRetirement { TenantId = TenantId, Issuer = TenantIssuer },
            WebOptions
        );
        var idResponse = await Client.GetAsync($"/directory/v1/tenants/retired-ids/{TenantId}");
        var issuerResponse = await Client.GetAsync(
            $"/directory/v1/tenants/retired-issuers?issuer={Uri.EscapeDataString(TenantIssuer)}"
        );

        Assert.Equal(HttpStatusCode.OK, retireResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, idResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, issuerResponse.StatusCode);
        Assert.Equal(TenantId, await ReadAsync<string>(issuerResponse));
    }

    [Fact]
    public async Task RetiredLookups_WhenNothingIsRetired_AnswerNotFound()
    {
        Authenticate();

        var idResponse = await Client.GetAsync($"/directory/v1/tenants/retired-ids/{TenantId}");
        var issuerResponse = await Client.GetAsync(
            $"/directory/v1/tenants/retired-issuers?issuer={Uri.EscapeDataString(TenantIssuer)}"
        );

        Assert.Equal(HttpStatusCode.NotFound, idResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, issuerResponse.StatusCode);
    }

    [Fact]
    public async Task RetiredIssuer_WithoutTheIssuerQuery_AnswersBadRequest()
    {
        Authenticate();

        var response = await Client.GetAsync("/directory/v1/tenants/retired-issuers");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeIssuer_WhenAnotherTenantHoldsTheIssuer_AnswersConflict()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);
        await ReserveTenantAsync(OtherTenantId, OtherTenantIssuer);

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/change-issuer",
            new TenantIssuerChange
            {
                TenantId = TenantId,
                PreviousIssuer = TenantIssuer,
                Issuer = OtherTenantIssuer,
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"Another tenant already has the issuer '{OtherTenantIssuer}'.",
            await ReadProblemDetailAsync(response)
        );
    }

    [Fact]
    public async Task ChangeIssuer_WhenTheIssuerIsFree_AnswersOk()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/change-issuer",
            new TenantIssuerChange
            {
                TenantId = TenantId,
                PreviousIssuer = TenantIssuer,
                Issuer = OtherTenantIssuer,
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Move Tenant Tests

    private async ValueTask SeedPlacedTenantAsync(
        string targetRegion,
        CancellationToken cancellationToken = default
    )
    {
        var services = (App ?? throw new InvalidOperationException()).Services;
        var cells = services.GetRequiredService<ICellDirectory>();
        var tenants = services.GetRequiredService<ITenantDirectory>();

        await cells.RegisterAsync(CreateCell("a"), cancellationToken);
        await cells.RegisterAsync(
            CreateCell("b") with
            {
                Region = targetRegion,
            },
            cancellationToken
        );
        await tenants.ReserveAsync(
            new TenantReservation
            {
                TenantId = TenantId,
                Issuer = TenantIssuer,
                Cell = "a",
            },
            cancellationToken
        );
        await tenants.PlaceAsync(
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = TenantIssuer,
                Cell = "a",
                Region = "region-a",
            },
            cancellationToken
        );
    }

    private async ValueTask<string?> GetPlacedCellAsync(
        CancellationToken cancellationToken = default
    )
    {
        var response = await Client.GetAsync(
            $"/directory/v1/tenants/{TenantId}",
            cancellationToken
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        return (await ReadAsync<TenantPlacement>(response, cancellationToken)).Cell;
    }

    [Fact]
    public async Task MoveTenant_WhenTheCallerIsTheTargetCell_AnswersOkAndMovesThePlacement()
    {
        await SeedPlacedTenantAsync("region-a");
        Authenticate(clientId: "cell-b");

        var response = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/move?fromCell=a&toCell=b",
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("b", await GetPlacedCellAsync());
    }

    [Fact]
    public async Task MoveTenant_WhenTheCallerIsAnotherCell_AnswersUnprocessableEntity()
    {
        await SeedPlacedTenantAsync("region-a");
        Authenticate(clientId: "cell-a");

        var response = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/move?fromCell=a&toCell=b",
            content: null
        );

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(
            "Only the target cell moves a tenant to itself.",
            await ReadProblemDetailAsync(response)
        );
        Assert.Equal("a", await GetPlacedCellAsync());
    }

    [Fact]
    public async Task MoveTenant_WithoutTheCells_AnswersUnprocessableEntity()
    {
        Authenticate(clientId: "cell-b");

        var response = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/move",
            content: null
        );

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task MoveTenant_WhenThePlacementIsInAnotherCell_AnswersConflictWithTheDetail()
    {
        await SeedPlacedTenantAsync("region-a");
        Authenticate(clientId: "cell-b");

        var response = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/move?fromCell=c&toCell=b",
            content: null
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"The tenant '{TenantId}' is not placed in the cell 'c'.",
            await ReadProblemDetailAsync(response)
        );
        Assert.Equal("a", await GetPlacedCellAsync());
    }

    [Fact]
    public async Task MoveTenant_WhenTheCellsAreInDifferentRegions_AnswersConflictWithTheDetail()
    {
        await SeedPlacedTenantAsync("region-b");
        Authenticate(clientId: "cell-b");

        var response = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/move?fromCell=a&toCell=b",
            content: null
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "The cells 'a' and 'b' are in different regions, and a tenant moves only within its region.",
            await ReadProblemDetailAsync(response)
        );
        Assert.Equal("a", await GetPlacedCellAsync());
    }

    #endregion

    #region Team Tests

    [Fact]
    public async Task ReserveTeam_WhenFree_AnswersOk()
    {
        Authenticate();

        var response = await Client.PostAsync(
            $"/directory/v1/teams/{TeamId}/reserve",
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReserveTeam_WhenHeld_AnswersConflictWithTheDetail()
    {
        Authenticate();
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/reserve", content: null);

        var response = await Client.PostAsync(
            $"/directory/v1/teams/{TeamId}/reserve",
            content: null
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"Another team already has the identifier '{TeamId}'.",
            await ReadProblemDetailAsync(response)
        );
    }

    [Fact]
    public async Task ReserveTeam_WhenRetired_AnswersConflictWithTheDetail()
    {
        Authenticate();
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/reserve", content: null);
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/retire", content: null);

        var response = await Client.PostAsync(
            $"/directory/v1/teams/{TeamId}/reserve",
            content: null
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"The team identifier '{TeamId}' belonged to a deleted team and is never reissued.",
            await ReadProblemDetailAsync(response)
        );
    }

    [Fact]
    public async Task PublishTeam_ThenGet_AnswersTheDocumentWithItsSequence()
    {
        Authenticate();
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/reserve", content: null);

        var publishResponse = await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{TeamId}/document",
            new TeamDocument { TeamId = TeamId, DisplayName = "Team One" },
            WebOptions
        );
        var getResponse = await Client.GetAsync($"/directory/v1/teams/{TeamId}/document");

        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var document = await ReadAsync<TeamDocument>(getResponse);
        Assert.Equal("Team One", document.DisplayName);
        Assert.True(document.Sequence > 0);
    }

    [Fact]
    public async Task PublishTeam_WhenTheDocumentIsForAnotherTeam_AnswersBadRequest()
    {
        Authenticate();

        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{TeamId}/document",
            new TeamDocument { TeamId = "team-2", DisplayName = "Team Two" },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTeamDocument_WhenNotPublished_AnswersNotFound()
    {
        Authenticate();

        var response = await Client.GetAsync($"/directory/v1/teams/{TeamId}/document");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RetiredTeam_BeforeAndAfterRetire_AnswersNotFoundThenOk()
    {
        Authenticate();
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/reserve", content: null);

        var before = await Client.GetAsync($"/directory/v1/teams/{TeamId}/retired");
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/retire", content: null);
        var after = await Client.GetAsync($"/directory/v1/teams/{TeamId}/retired");

        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task ReleaseTeam_WhenReserved_FreesTheIdentifier()
    {
        Authenticate();
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/reserve", content: null);

        var release = await Client.PostAsync(
            $"/directory/v1/teams/{TeamId}/release",
            content: null
        );
        var reserve = await Client.PostAsync(
            $"/directory/v1/teams/{TeamId}/reserve",
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reserve.StatusCode);
    }

    [Fact]
    public async Task ListChanges_WithAfterAndLimit_PagesTheChanges()
    {
        Authenticate();
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/reserve", content: null);
        await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{TeamId}/document",
            new TeamDocument { TeamId = TeamId, DisplayName = "Team One" },
            WebOptions
        );
        await Client.PutAsJsonAsync(
            "/directory/v1/platform/document",
            new PlatformDocument { SettingsJson = "{\"max_tenants\":5}" },
            WebOptions
        );

        var firstPage = await ReadAsync<List<DirectoryChange>>(
            await Client.GetAsync("/directory/v1/teams/changes?after=0&limit=1")
        );
        var secondPage = await ReadAsync<List<DirectoryChange>>(
            await Client.GetAsync(
                $"/directory/v1/teams/changes?after={firstPage[0].Sequence}&limit=10"
            )
        );

        var teamChange = Assert.Single(firstPage);
        Assert.Equal(TeamId, teamChange.TeamId);
        Assert.NotNull(teamChange.Document);
        var platformChange = Assert.Single(secondPage);
        Assert.Null(platformChange.TeamId);
        Assert.NotNull(platformChange.Platform);
        Assert.True(platformChange.Sequence > teamChange.Sequence);
    }

    #endregion

    #region Platform Tests

    [Fact]
    public async Task GetPlatformDocument_WhenNotPublished_AnswersNotFound()
    {
        Authenticate();

        var response = await Client.GetAsync("/directory/v1/platform/document");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublishPlatformDocument_ThenGet_AnswersTheDocument()
    {
        Authenticate();

        var publishResponse = await Client.PutAsJsonAsync(
            "/directory/v1/platform/document",
            new PlatformDocument { SettingsJson = "{\"max_tenants\":5}" },
            WebOptions
        );
        var getResponse = await Client.GetAsync("/directory/v1/platform/document");

        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var document = await ReadAsync<PlatformDocument>(getResponse);
        Assert.Equal("{\"max_tenants\":5}", document.SettingsJson);
        Assert.True(document.Sequence > 0);
    }

    #endregion

    #region Cell Tests

    private static CellDescriptor CreateCell(string name) =>
        new()
        {
            Name = name,
            Region = "region-a",
            Kind = CellKinds.Shared,
            ManagementUrl = $"https://{name}.example/",
        };

    [Fact]
    public async Task RegisterCell_WhenTheCallerIsTheCell_RegistersItAndListsIt()
    {
        Authenticate(clientId: "cell-a");

        var putResponse = await Client.PutAsJsonAsync(
            "/directory/v1/cells/a",
            CreateCell("a"),
            WebOptions
        );
        var listResponse = await Client.GetAsync("/directory/v1/cells");
        var getResponse = await Client.GetAsync("/directory/v1/cells/a");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var cells = await ReadAsync<List<CellDescriptor>>(listResponse);
        Assert.Equal("a", Assert.Single(cells).Name);
        var cell = await ReadAsync<CellDescriptor>(getResponse);
        Assert.Equal("https://a.example/", cell.ManagementUrl);
    }

    [Fact]
    public async Task RegisterCell_WhenTheCallerIsAnotherCell_AnswersForbidden()
    {
        Authenticate(clientId: "cell-b");

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/cells/a",
            CreateCell("a"),
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var listResponse = await Client.GetAsync("/directory/v1/cells");
        Assert.Empty(await ReadAsync<List<CellDescriptor>>(listResponse));
    }

    [Fact]
    public async Task RegisterCell_WhenTheDescriptorNamesAnotherCell_AnswersBadRequest()
    {
        Authenticate(clientId: "cell-a");

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/cells/a",
            CreateCell("b"),
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCell_WhenNotRegistered_AnswersNotFound()
    {
        Authenticate();

        var response = await Client.GetAsync("/directory/v1/cells/a");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}
