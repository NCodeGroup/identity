// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Client;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryEndpointsTests : IAsyncLifetime
{
    private const string ServerIssuer = "https://control-plane.example";
    private const string Audience = GlobalDirectoryProtocol.DirectoryAudience;
    private const string Token = "directory-access-token";
    private const string CellName = "a";
    private const string ControlPlaneCell = "control";
    private const string DirectoryScope = GlobalDirectoryProtocol.DirectoryScope;
    private const string TenantId = "tenant-1";
    private const string OtherTenantId = "tenant-2";
    private const string TenantIssuer = "https://op.example/tenant-1";
    private const string OtherTenantIssuer = "https://op.example/tenant-2";
    private const string TeamId = "team-1";

    private const string InvalidManagementUrlDetail =
        "The cell's management URL is an absolute https URL, or an http URL in local development, with no user "
        + "information, query or fragment.";

    private const string InvalidIssuerDetail =
        "A tenant's issuer is an absolute https URL, or an http URL on a loopback host, with no user information, "
        + "query or fragment.";

    private const string SettingsNotAnObjectDetail = "The document's settings are a JSON object.";

    private const string BlankTeamDetail = "A team identifier is not blank.";

    private static JsonSerializerOptions WebOptions { get; } = new(JsonSerializerDefaults.Web);

    private static string CellTooLongDetail =>
        $"A cell's kind is at most {DirectoryLimits.MaxCellKindLength} characters and its management URL at most "
        + $"{DirectoryLimits.MaxManagementUrlLength}.";

    private static string TeamTooLongDetail =>
        $"A team identifier is at most {DirectoryLimits.MaxIdentifierLength} characters.";

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

        var app = await CreateAppAsync(configure: null);

        App = app;
        ClientOrNull = app.GetTestClient();
    }

    private async ValueTask<WebApplication> CreateAppAsync(
        Action<IServiceCollection>? configure,
        CancellationToken cancellationToken = default
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DirectoryServer:Issuer"] = ServerIssuer,
                ["DirectoryServer:ControlPlaneCell"] = ControlPlaneCell,
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
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapDirectory();

        var dbContextFactory = app.Services.GetRequiredService<
            IDbContextFactory<DirectoryDbContext>
        >();
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        await app.StartAsync(cancellationToken);

        return app;
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

    private static string ClientIdOf(string cell) =>
        GlobalDirectoryProtocol.CellClientIdPrefix + cell;

    private void Authenticate(string? clientId = null, string scope = DirectoryScope)
    {
        var effectiveClientId = clientId ?? ClientIdOf(CellName);
        ValidatorMock
            .Setup(x =>
                x.ValidateAsync(ServerIssuer, Audience, Token, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateValidResult(effectiveClientId, scope))
            .Verifiable();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    private void AuthenticateAsControlPlane() => Authenticate(ClientIdOf(ControlPlaneCell));

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
        string? teamId = null,
        CancellationToken cancellationToken = default
    ) =>
        await Client.PostAsJsonAsync(
            "/directory/v1/tenants/reserve",
            new TenantReservation
            {
                TenantId = tenantId,
                Issuer = issuer,
                TeamId = teamId,
                Cell = CellName,
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
    public async Task Request_WithAKeyBoundToken_AnswersUnauthorized()
    {
        var payload = JsonElement.Parse(
            JsonSerializer.Serialize(
                new
                {
                    client_id = ClientIdOf(CellName),
                    scope = DirectoryScope,
                    cnf = new { jkt = "x" },
                }
            )
        );
        ValidatorMock
            .Setup(x =>
                x.ValidateAsync(ServerIssuer, Audience, Token, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new IssuerAccessTokenResult(payload, ServerIssuer))
            .Verifiable();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var response = await Client.GetAsync("/directory/v1/cells");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Request_WhenTheIssuerIsUnavailable_AnswersServiceUnavailableWithTheWaitInHeaderAndMember()
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
        Assert.Equal(
            TimeSpan.FromSeconds(GlobalDirectoryProtocol.UnavailableRetryAfterSeconds),
            response.Headers.RetryAfter.Delta
        );
        var problem = await ReadAsync<JsonElement>(response);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            GlobalDirectoryProtocol.UnavailableRetryAfterSeconds,
            problem.GetProperty(RetryAfterProblem.MemberName).GetInt32()
        );
        Assert.Equal(
            "The issuer of the directory's access tokens is unavailable. Retry the request.",
            problem.GetProperty("detail").GetString()
        );
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
    public async Task ReserveTenant_WhenTheCellIsNotACellLabel_AnswersBadRequest()
    {
        Authenticate();

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/reserve",
            new TenantReservation
            {
                TenantId = TenantId,
                Issuer = TenantIssuer,
                Cell = "Cell-A",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var reserveResponse = await ReserveTenantAsync(TenantId, TenantIssuer);
        Assert.Equal(HttpStatusCode.OK, reserveResponse.StatusCode);
    }

    [Fact]
    public async Task ReserveTenant_WhenTheTenantIdIsOverMaxLength_AnswersBadRequest()
    {
        Authenticate();
        var tenantId = new string('t', DirectoryLimits.MaxIdentifierLength + 1);

        var response = await ReserveTenantAsync(tenantId, TenantIssuer);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            $"at most {DirectoryLimits.MaxIdentifierLength} characters",
            await ReadProblemDetailAsync(response),
            StringComparison.Ordinal
        );
        var reserveResponse = await ReserveTenantAsync(TenantId, TenantIssuer);
        Assert.Equal(HttpStatusCode.OK, reserveResponse.StatusCode);
    }

    [Fact]
    public async Task ReserveTenant_WhenTheTeamIdIsOverMaxLength_AnswersBadRequest()
    {
        Authenticate();
        var teamId = new string('t', DirectoryLimits.MaxIdentifierLength + 1);

        var response = await ReserveTenantAsync(TenantId, TenantIssuer, teamId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TeamTooLongDetail, await ReadProblemDetailAsync(response));
        var reserveResponse = await ReserveTenantAsync(TenantId, TenantIssuer);
        Assert.Equal(HttpStatusCode.OK, reserveResponse.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReserveTenant_WhenTheTeamIdIsBlank_AnswersBadRequest(string teamId)
    {
        Authenticate();

        var response = await ReserveTenantAsync(TenantId, TenantIssuer, teamId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(BlankTeamDetail, await ReadProblemDetailAsync(response));
        var reserveResponse = await ReserveTenantAsync(TenantId, TenantIssuer);
        Assert.Equal(HttpStatusCode.OK, reserveResponse.StatusCode);
    }

    [Fact]
    public async Task PlaceTenant_WhenTheRegionIsNotACellLabel_Returns400()
    {
        Authenticate();
        var cellResponse = await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{CellName}",
            CreateCell(CellName),
            WebOptions
        );
        Assert.Equal(HttpStatusCode.OK, cellResponse.StatusCode);
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/place",
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = TenantIssuer,
                Cell = CellName,
                Region = "West-Europe",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var getResponse = await Client.GetAsync($"/directory/v1/tenants/{TenantId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task PlaceTenant_WhenTheIssuerIsOverMaxLength_AnswersBadRequest()
    {
        Authenticate();
        var cellResponse = await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{CellName}",
            CreateCell(CellName),
            WebOptions
        );
        Assert.Equal(HttpStatusCode.OK, cellResponse.StatusCode);
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/place",
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = "https://op.example/" + new string('i', DirectoryLimits.MaxIssuerLength),
                Cell = CellName,
                Region = "region-a",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var getResponse = await Client.GetAsync($"/directory/v1/tenants/{TenantId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task PlaceTenant_ThenGet_AnswersThePlacementInCamelCase()
    {
        Authenticate();
        var cellResponse = await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{CellName}",
            CreateCell(CellName),
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
                Cell = CellName,
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
    public async Task PlaceTenant_WhenNotReserved_Returns409()
    {
        Authenticate();
        var cellResponse = await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{CellName}",
            CreateCell(CellName),
            WebOptions
        );
        Assert.Equal(HttpStatusCode.OK, cellResponse.StatusCode);

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/place",
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = TenantIssuer,
                Cell = CellName,
                Region = "region-a",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"The tenant '{TenantId}' is not reserved.",
            await ReadProblemDetailAsync(response)
        );
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

    [Fact]
    public async Task RetireTenant_WhenTheTenantIdIsOverMaxLength_AnswersBadRequest()
    {
        Authenticate();
        var tenantId = new string('t', DirectoryLimits.MaxIdentifierLength + 1);

        var response = await RetireTenantAsync(tenantId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var issuerResponse = await Client.GetAsync(
            $"/directory/v1/tenants/retired-issuers?issuer={Uri.EscapeDataString(TenantIssuer)}"
        );
        Assert.Equal(HttpStatusCode.NotFound, issuerResponse.StatusCode);
    }

    [Fact]
    public async Task ChangeIssuer_WhenTheIssuerIsOverMaxLength_AnswersBadRequest()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/change-issuer",
            new TenantIssuerChange
            {
                TenantId = TenantId,
                PreviousIssuer = TenantIssuer,
                Issuer = "https://op.example/" + new string('i', DirectoryLimits.MaxIssuerLength),
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var issuerResponse = await Client.GetAsync(
            $"/directory/v1/tenants/retired-issuers?issuer={Uri.EscapeDataString(TenantIssuer)}"
        );
        Assert.Equal(HttpStatusCode.NotFound, issuerResponse.StatusCode);
    }

    [Theory]
    [InlineData("http://op.example/tenant-1")]
    [InlineData("https://user@op.example/tenant-1")]
    [InlineData("https://@op.example/tenant-1")]
    [InlineData("https://op.example/tenant-1?x=1")]
    [InlineData("https://op.example/tenant-1#f")]
    [InlineData("op.example/tenant-1")]
    [InlineData(" https://op.example/tenant-1")]
    public async Task ReserveTenant_WhenTheIssuerIsNotAServiceUrl_AnswersBadRequestWithTheDetail(
        string issuer
    )
    {
        Authenticate();

        var response = await ReserveTenantAsync(TenantId, issuer);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvalidIssuerDetail, await ReadProblemDetailAsync(response));
        var reserveResponse = await ReserveTenantAsync(TenantId, TenantIssuer);
        Assert.Equal(HttpStatusCode.OK, reserveResponse.StatusCode);
    }

    [Fact]
    public async Task ReserveTenant_WhenTheIssuerIsAnHttpUrlOnALoopbackHost_AnswersOk()
    {
        Authenticate();

        var response = await ReserveTenantAsync(TenantId, "http://localhost:5000/tenant-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlaceTenant_WhenTheIssuerIsNotAServiceUrl_AnswersBadRequestWithTheDetail()
    {
        Authenticate();
        var cellResponse = await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{CellName}",
            CreateCell(CellName),
            WebOptions
        );
        Assert.Equal(HttpStatusCode.OK, cellResponse.StatusCode);
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/place",
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = "https://op.example/tenant-1?x=1",
                Cell = CellName,
                Region = "region-a",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvalidIssuerDetail, await ReadProblemDetailAsync(response));
        var getResponse = await Client.GetAsync($"/directory/v1/tenants/{TenantId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Theory]
    [InlineData("http://op.example/tenant-2")]
    [InlineData("https://op.example/tenant-2#f")]
    [InlineData("")]
    public async Task ChangeIssuer_WhenTheIssuerIsNotAServiceUrl_AnswersBadRequestWithTheDetail(
        string issuer
    )
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/change-issuer",
            new TenantIssuerChange
            {
                TenantId = TenantId,
                PreviousIssuer = TenantIssuer,
                Issuer = issuer,
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvalidIssuerDetail, await ReadProblemDetailAsync(response));
        var issuerResponse = await Client.GetAsync(
            $"/directory/v1/tenants/retired-issuers?issuer={Uri.EscapeDataString(TenantIssuer)}"
        );
        Assert.Equal(HttpStatusCode.NotFound, issuerResponse.StatusCode);
    }

    [Fact]
    public async Task ChangeIssuer_WhenTheIssuerIsNull_AnswersOk()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/change-issuer",
            new TenantIssuerChange
            {
                TenantId = TenantId,
                PreviousIssuer = TenantIssuer,
                Issuer = null,
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var issuerResponse = await Client.GetAsync(
            $"/directory/v1/tenants/retired-issuers?issuer={Uri.EscapeDataString(TenantIssuer)}"
        );
        Assert.Equal(HttpStatusCode.OK, issuerResponse.StatusCode);
    }

    [Fact]
    public async Task RetireTenant_WhenTheIssuerIsNotAServiceUrl_AnswersBadRequestWithTheDetail()
    {
        AuthenticateAsControlPlane();
        const string issuer = "https://op.example/tenant-1#f";

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/retire",
            new TenantRetirement { TenantId = TenantId, Issuer = issuer },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvalidIssuerDetail, await ReadProblemDetailAsync(response));
        var idResponse = await Client.GetAsync($"/directory/v1/tenants/retired-ids/{TenantId}");
        Assert.Equal(HttpStatusCode.NotFound, idResponse.StatusCode);
        var issuerResponse = await Client.GetAsync(
            $"/directory/v1/tenants/retired-issuers?issuer={Uri.EscapeDataString(issuer)}"
        );
        Assert.Equal(HttpStatusCode.NotFound, issuerResponse.StatusCode);
    }

    #endregion

    #region Tenant Write Authorization Tests

    private async ValueTask<HttpResponseMessage> ReleaseTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default
    ) =>
        await Client.PostAsync(
            $"/directory/v1/tenants/{tenantId}/release",
            content: null,
            cancellationToken
        );

    private async ValueTask<HttpResponseMessage> RetireTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default
    ) =>
        await Client.PostAsJsonAsync(
            "/directory/v1/tenants/retire",
            new TenantRetirement { TenantId = tenantId, Issuer = TenantIssuer },
            WebOptions,
            cancellationToken
        );

    private async ValueTask<HttpResponseMessage> ChangeIssuerAsync(
        string tenantId,
        CancellationToken cancellationToken = default
    ) =>
        await Client.PostAsJsonAsync(
            "/directory/v1/tenants/change-issuer",
            new TenantIssuerChange
            {
                TenantId = tenantId,
                PreviousIssuer = TenantIssuer,
                Issuer = OtherTenantIssuer,
            },
            WebOptions,
            cancellationToken
        );

    private async ValueTask AssertTenantIsStillHeldAsync(
        CancellationToken cancellationToken = default
    )
    {
        Authenticate();

        var response = await ReserveTenantAsync(
            TenantId,
            OtherTenantIssuer,
            cancellationToken: cancellationToken
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"Another tenant already has the identifier '{TenantId}'.",
            await ReadProblemDetailAsync(response, cancellationToken)
        );
    }

    [Fact]
    public async Task ReserveTenant_WhenCalledByAnotherCell_Returns403()
    {
        Authenticate(ClientIdOf("b"));

        var response = await ReserveTenantAsync(TenantId, TenantIssuer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "A cell reserves a tenant only for itself.",
            await ReadProblemDetailAsync(response)
        );
    }

    [Fact]
    public async Task ReserveTenant_WhenCalledByTheControlPlaneForAnotherCell_Returns403()
    {
        AuthenticateAsControlPlane();

        var response = await ReserveTenantAsync(TenantId, TenantIssuer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReserveTenant_WhenTheCallerLacksTheCellPrefix_Returns403()
    {
        Authenticate(CellName);

        var response = await ReserveTenantAsync(TenantId, TenantIssuer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlaceTenant_WhenCalledByAnotherCell_Returns403()
    {
        Authenticate(ClientIdOf("b"));

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/place",
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = TenantIssuer,
                Cell = CellName,
                Region = "region-a",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "A cell places a tenant only in itself.",
            await ReadProblemDetailAsync(response)
        );
    }

    [Fact]
    public async Task PlaceTenant_WhenCalledByTheControlPlaneForAnotherCell_Returns403()
    {
        AuthenticateAsControlPlane();

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/place",
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = TenantIssuer,
                Cell = CellName,
                Region = "region-a",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseTenant_WhenCalledByAnotherCell_Returns403()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);
        Authenticate(ClientIdOf("b"));

        var response = await ReleaseTenantAsync(TenantId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertTenantIsStillHeldAsync();
    }

    [Fact]
    public async Task ReleaseTenant_WhenTheCallerLacksTheCellPrefix_Returns403()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);
        Authenticate(CellName);

        var response = await ReleaseTenantAsync(TenantId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseTenant_WhenCalledByTheControlPlane_FreesTheIdentifier()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);
        AuthenticateAsControlPlane();

        var response = await ReleaseTenantAsync(TenantId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Authenticate();
        Assert.Equal(
            HttpStatusCode.OK,
            (await ReserveTenantAsync(TenantId, TenantIssuer)).StatusCode
        );
    }

    [Fact]
    public async Task ReleaseTenant_WhenTheTenantHasNoRecord_AnswersOk()
    {
        Authenticate(ClientIdOf("b"));

        var response = await ReleaseTenantAsync(TenantId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RetireTenant_WhenCalledByAnotherCell_Returns403()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);
        Authenticate(ClientIdOf("b"));

        var response = await RetireTenantAsync(TenantId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertTenantIsStillHeldAsync();
    }

    [Fact]
    public async Task RetireTenant_WhenCalledByTheControlPlane_AnswersOk()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);
        AuthenticateAsControlPlane();

        var response = await RetireTenantAsync(TenantId);
        var idResponse = await Client.GetAsync($"/directory/v1/tenants/retired-ids/{TenantId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, idResponse.StatusCode);
    }

    [Fact]
    public async Task RetireTenant_WhenTheRetiredRecordKeepsTheCellAndAnotherCellRetriesIt_Returns403()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);
        await RetireTenantAsync(TenantId);
        Authenticate(ClientIdOf("b"));

        var response = await RetireTenantAsync(TenantId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ChangeIssuer_WhenCalledByAnotherCell_Returns403()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);
        Authenticate(ClientIdOf("b"));

        var response = await ChangeIssuerAsync(TenantId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ChangeIssuer_WhenCalledByTheControlPlane_AnswersOk()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);
        AuthenticateAsControlPlane();

        var response = await ChangeIssuerAsync(TenantId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseTenant_WhenTheTenantHasNoRecordAndTheCallerLacksTheCellPrefix_Returns403()
    {
        Authenticate(CellName);

        var response = await ReleaseTenantAsync(TenantId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RetireTenant_WhenAnotherCellRetiresAnUnknownTenant_Returns403()
    {
        Authenticate(ClientIdOf("b"));

        var response = await RetireTenantAsync(TenantId);
        AuthenticateAsControlPlane();
        var idResponse = await Client.GetAsync($"/directory/v1/tenants/retired-ids/{TenantId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, idResponse.StatusCode);
    }

    [Fact]
    public async Task RetireTenant_WhenTheControlPlaneRetiresAnUnknownTenant_Returns200()
    {
        AuthenticateAsControlPlane();

        var response = await RetireTenantAsync(TenantId);
        var idResponse = await Client.GetAsync($"/directory/v1/tenants/retired-ids/{TenantId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, idResponse.StatusCode);
    }

    [Fact]
    public async Task ChangeIssuer_WhenThePreviousIssuerIsNotTheStoredIssuer_Returns409()
    {
        Authenticate();
        await ReserveTenantAsync(TenantId, TenantIssuer);

        var response = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/change-issuer",
            new TenantIssuerChange
            {
                TenantId = TenantId,
                PreviousIssuer = OtherTenantIssuer,
                Issuer = "https://op.example/tenant-3",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"The tenant '{TenantId}' does not hold the issuer '{OtherTenantIssuer}'.",
            await ReadProblemDetailAsync(response)
        );
    }

    [Fact]
    public async Task ChangeIssuer_WhenTheControlPlaneChangesTheIssuerOfAnUnknownTenant_Returns404()
    {
        AuthenticateAsControlPlane();

        var response = await ChangeIssuerAsync(TenantId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    private async ValueTask OfferMoveAsync(
        string fromCell,
        string toCell,
        CancellationToken cancellationToken = default
    )
    {
        var services = (App ?? throw new InvalidOperationException()).Services;
        var tenants = services.GetRequiredService<ITenantDirectory>();

        var result = await tenants.OfferMoveAsync(TenantId, fromCell, toCell, cancellationToken);
        Assert.True(result.IsOffered);
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
        await OfferMoveAsync("a", "b");
        Authenticate(clientId: "cell-b");

        var response = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/move?fromCell=a&toCell=b",
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("b", await GetPlacedCellAsync());
    }

    [Fact]
    public async Task MoveTenant_WithoutAnOffer_AnswersConflictWithTheDetail()
    {
        await SeedPlacedTenantAsync("region-a");
        Authenticate(clientId: "cell-b");

        var response = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/move?fromCell=a&toCell=b",
            content: null
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"The cell 'a' has not offered the tenant '{TenantId}' to the cell 'b'.",
            await ReadProblemDetailAsync(response)
        );
        Assert.Equal("a", await GetPlacedCellAsync());
    }

    [Fact]
    public async Task MoveTenant_CallerIsNotTargetCell_Returns403()
    {
        await SeedPlacedTenantAsync("region-a");
        await OfferMoveAsync("a", "b");
        Authenticate(clientId: "cell-a");

        var response = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/move?fromCell=a&toCell=b",
            content: null
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "Only the target cell moves a tenant to itself.",
            await ReadProblemDetailAsync(response)
        );
        Assert.Equal("a", await GetPlacedCellAsync());
    }

    [Fact]
    public async Task MoveTenant_WithoutTheCells_AnswersBadRequest()
    {
        Authenticate(clientId: "cell-b");

        var response = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/move",
            content: null
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MoveTenant_WhenACellIsNotACellLabel_AnswersBadRequest()
    {
        await SeedPlacedTenantAsync("region-a");
        Authenticate(clientId: "cell-b");

        var response = await Client.PostAsync(
            $"/directory/v1/tenants/{TenantId}/move?fromCell=A&toCell=b",
            content: null
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("a", await GetPlacedCellAsync());
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

    #region Offer Move Tests

    [Fact]
    public async Task OfferMove_SourceCell_Returns204()
    {
        await SeedPlacedTenantAsync("region-a");
        Authenticate(clientId: "cell-a");

        var response = await Client.PostAsJsonAsync(
            $"/directory/v1/tenants/{TenantId}/move-offers",
            new TenantMoveOfferRequest { ToCell = "b" },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task OfferMove_CallerIsNotSourceCell_Returns403()
    {
        await SeedPlacedTenantAsync("region-a");
        Authenticate(clientId: "cell-b");

        var response = await Client.PostAsJsonAsync(
            $"/directory/v1/tenants/{TenantId}/move-offers",
            new TenantMoveOfferRequest { ToCell = "b" },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "Only the cell that holds a tenant offers it to another cell.",
            await ReadProblemDetailAsync(response)
        );
    }

    [Fact]
    public async Task OfferMove_WhenTheTargetCellIsNotACellLabel_AnswersBadRequest()
    {
        await SeedPlacedTenantAsync("region-a");
        Authenticate(clientId: "cell-a");

        var response = await Client.PostAsJsonAsync(
            $"/directory/v1/tenants/{TenantId}/move-offers",
            new TenantMoveOfferRequest { ToCell = "B" },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OfferMove_WhenTheCellsAreInDifferentRegions_AnswersConflictWithTheDetail()
    {
        await SeedPlacedTenantAsync("region-b");
        Authenticate(clientId: "cell-a");

        var response = await Client.PostAsJsonAsync(
            $"/directory/v1/tenants/{TenantId}/move-offers",
            new TenantMoveOfferRequest { ToCell = "b" },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "The cells 'a' and 'b' are in different regions, and a tenant moves only within its region.",
            await ReadProblemDetailAsync(response)
        );
    }

    #endregion

    #region Team Tests

    [Fact]
    public async Task ReserveTeam_WhenFree_AnswersOk()
    {
        AuthenticateAsControlPlane();

        var response = await Client.PostAsync(
            $"/directory/v1/teams/{TeamId}/reserve",
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReserveTeam_WhenHeld_AnswersConflictWithTheDetail()
    {
        AuthenticateAsControlPlane();
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
        AuthenticateAsControlPlane();
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
    public async Task ReserveTeam_WhenTheTeamIdIsOverMaxLength_AnswersBadRequest()
    {
        AuthenticateAsControlPlane();
        var teamId = new string('t', DirectoryLimits.MaxIdentifierLength + 1);

        var response = await Client.PostAsync(
            $"/directory/v1/teams/{teamId}/reserve",
            content: null
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TeamTooLongDetail, await ReadProblemDetailAsync(response));
    }

    [Fact]
    public async Task ReserveTeam_WhenTheTeamIdIsBlank_AnswersBadRequest()
    {
        AuthenticateAsControlPlane();

        var response = await Client.PostAsync("/directory/v1/teams/%20/reserve", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The team is required.", await ReadProblemDetailAsync(response));
    }

    [Fact]
    public async Task ReleaseTeam_WhenTheTeamIdIsBlank_AnswersBadRequest()
    {
        AuthenticateAsControlPlane();

        var response = await Client.PostAsync("/directory/v1/teams/%20/release", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The team is required.", await ReadProblemDetailAsync(response));
    }

    [Fact]
    public async Task RetireTeam_WhenTheTeamIdIsBlank_AnswersBadRequest()
    {
        AuthenticateAsControlPlane();

        var response = await Client.PostAsync("/directory/v1/teams/%20/retire", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The team is required.", await ReadProblemDetailAsync(response));
    }

    [Fact]
    public async Task PublishTeam_ThenGet_AnswersTheDocumentWithItsSequence()
    {
        AuthenticateAsControlPlane();
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/reserve", content: null);

        var publishResponse = await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{TeamId}/document",
            new TeamDocument
            {
                TeamId = TeamId,
                DisplayName = "Team One",
                IsDisabled = false,
            },
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
        AuthenticateAsControlPlane();

        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{TeamId}/document",
            new TeamDocument
            {
                TeamId = "team-2",
                DisplayName = "Team Two",
                IsDisabled = false,
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PublishTeam_WhenTheTeamIdIsOverMaxLength_AnswersBadRequest()
    {
        AuthenticateAsControlPlane();
        var teamId = new string('t', DirectoryLimits.MaxIdentifierLength + 1);

        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{teamId}/document",
            new TeamDocument
            {
                TeamId = teamId,
                DisplayName = "Team One",
                IsDisabled = false,
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TeamTooLongDetail, await ReadProblemDetailAsync(response));
    }

    [Fact]
    public async Task PublishTeam_WhenTheParentTeamIdIsOverMaxLength_AnswersBadRequest()
    {
        AuthenticateAsControlPlane();

        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{TeamId}/document",
            new TeamDocument
            {
                TeamId = TeamId,
                DisplayName = "Team One",
                IsDisabled = false,
                ParentTeamId = new string('t', DirectoryLimits.MaxIdentifierLength + 1),
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TeamTooLongDetail, await ReadProblemDetailAsync(response));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishTeam_WhenTheParentTeamIdIsBlank_AnswersBadRequest(string parentTeamId)
    {
        AuthenticateAsControlPlane();

        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{TeamId}/document",
            new TeamDocument
            {
                TeamId = TeamId,
                DisplayName = "Team One",
                IsDisabled = false,
                ParentTeamId = parentTeamId,
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(BlankTeamDetail, await ReadProblemDetailAsync(response));
        var getResponse = await Client.GetAsync($"/directory/v1/teams/{TeamId}/document");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("null")]
    [InlineData("\"{}\"")]
    [InlineData("5")]
    public async Task PublishTeam_WhenTheSettingsAreNotAnObject_AnswersBadRequest(
        string settingsJson
    )
    {
        AuthenticateAsControlPlane();

        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{TeamId}/document",
            new TeamDocument
            {
                TeamId = TeamId,
                DisplayName = "Team One",
                IsDisabled = false,
                Settings = JsonElement.Parse(settingsJson),
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SettingsNotAnObjectDetail, await ReadProblemDetailAsync(response));
        var getResponse = await Client.GetAsync($"/directory/v1/teams/{TeamId}/document");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task PublishTeam_WhenTheTeamWouldBeNestedDeeperThanTheLimit_AnswersBadRequest()
    {
        AuthenticateAsControlPlane();
        for (var index = 1; index <= DirectoryLimits.MaxTeamDepth; index++)
        {
            var atLimit = await Client.PutAsJsonAsync(
                $"/directory/v1/teams/chain-{index}/document",
                new TeamDocument
                {
                    TeamId = $"chain-{index}",
                    DisplayName = "Chain",
                    IsDisabled = false,
                    ParentTeamId = index > 1 ? $"chain-{index - 1}" : null,
                },
                WebOptions
            );
            Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
        }

        var tooDeepTeamId = $"chain-{DirectoryLimits.MaxTeamDepth + 1}";
        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{tooDeepTeamId}/document",
            new TeamDocument
            {
                TeamId = tooDeepTeamId,
                DisplayName = "Chain",
                IsDisabled = false,
                ParentTeamId = $"chain-{DirectoryLimits.MaxTeamDepth}",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            $"The team '{tooDeepTeamId}' cannot be nested deeper than {DirectoryLimits.MaxTeamDepth} levels.",
            await ReadProblemDetailAsync(response)
        );
        var getResponse = await Client.GetAsync($"/directory/v1/teams/{tooDeepTeamId}/document");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetTeamDocument_WhenNotPublished_AnswersNotFound()
    {
        Authenticate();

        var response = await Client.GetAsync($"/directory/v1/teams/{TeamId}/document");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTeamDocument_WhenTheTeamIsUnknown_ReturnsANotFoundProblemDocument()
    {
        Authenticate();

        var response = await Client.GetAsync($"/directory/v1/teams/{TeamId}/document");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var detail = await ReadProblemDetailAsync(response);
        Assert.Contains(TeamId, detail);
    }

    [Fact]
    public async Task RetiredTeam_BeforeAndAfterRetire_AnswersNotFoundThenOk()
    {
        AuthenticateAsControlPlane();
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
        AuthenticateAsControlPlane();
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
    public async Task ReleaseTeam_WhenTheTeamIdIsOverMaxLength_AnswersBadRequest()
    {
        AuthenticateAsControlPlane();
        var teamId = new string('t', DirectoryLimits.MaxIdentifierLength + 1);

        var response = await Client.PostAsync(
            $"/directory/v1/teams/{teamId}/release",
            content: null
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TeamTooLongDetail, await ReadProblemDetailAsync(response));
    }

    [Fact]
    public async Task ReserveTeam_WhenCalledByAnotherCell_Returns403()
    {
        Authenticate();

        var response = await Client.PostAsync(
            $"/directory/v1/teams/{TeamId}/reserve",
            content: null
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "Only the control-plane cell writes the platform and the teams.",
            await ReadProblemDetailAsync(response)
        );
    }

    [Fact]
    public async Task PublishTeam_WhenCalledByAnotherCell_Returns403()
    {
        Authenticate();

        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{TeamId}/document",
            new TeamDocument
            {
                TeamId = TeamId,
                DisplayName = "Team One",
                IsDisabled = false,
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseTeam_WhenCalledByAnotherCell_Returns403()
    {
        AuthenticateAsControlPlane();
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/reserve", content: null);
        Authenticate();

        var response = await Client.PostAsync(
            $"/directory/v1/teams/{TeamId}/release",
            content: null
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AuthenticateAsControlPlane();
        var reserve = await Client.PostAsync(
            $"/directory/v1/teams/{TeamId}/reserve",
            content: null
        );
        Assert.Equal(HttpStatusCode.Conflict, reserve.StatusCode);
    }

    [Fact]
    public async Task RetireTeam_WhenCalledByAnotherCell_Returns403()
    {
        AuthenticateAsControlPlane();
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/reserve", content: null);
        Authenticate();

        var response = await Client.PostAsync(
            $"/directory/v1/teams/{TeamId}/retire",
            content: null
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var retired = await Client.GetAsync($"/directory/v1/teams/{TeamId}/retired");
        Assert.Equal(HttpStatusCode.NotFound, retired.StatusCode);
    }

    [Fact]
    public async Task RetireTeam_WhenTheTeamIdIsOverMaxLength_AnswersBadRequest()
    {
        AuthenticateAsControlPlane();
        var teamId = new string('t', DirectoryLimits.MaxIdentifierLength + 1);

        var response = await Client.PostAsync(
            $"/directory/v1/teams/{teamId}/retire",
            content: null
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TeamTooLongDetail, await ReadProblemDetailAsync(response));
    }

    [Fact]
    public async Task ListChanges_WithAfterAndLimit_PagesTheChanges()
    {
        AuthenticateAsControlPlane();
        await Client.PostAsync($"/directory/v1/teams/{TeamId}/reserve", content: null);
        await Client.PutAsJsonAsync(
            $"/directory/v1/teams/{TeamId}/document",
            new TeamDocument
            {
                TeamId = TeamId,
                DisplayName = "Team One",
                IsDisabled = false,
            },
            WebOptions
        );
        await Client.PutAsJsonAsync(
            "/directory/v1/platform/document",
            new PlatformDocument { Settings = JsonElement.Parse("""{"max_tenants":5}""") },
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
        AuthenticateAsControlPlane();

        var publishResponse = await Client.PutAsJsonAsync(
            "/directory/v1/platform/document",
            new PlatformDocument { Settings = JsonElement.Parse("""{"max_tenants":5}""") },
            WebOptions
        );
        var getResponse = await Client.GetAsync("/directory/v1/platform/document");

        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var document = await ReadAsync<PlatformDocument>(getResponse);
        Assert.Equal("""{"max_tenants":5}""", document.Settings.GetRawText());
        Assert.True(document.Sequence > 0);
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("null")]
    [InlineData("\"{}\"")]
    [InlineData("5")]
    public async Task PublishPlatformDocument_WhenTheSettingsAreNotAnObject_AnswersBadRequest(
        string settingsJson
    )
    {
        AuthenticateAsControlPlane();

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/platform/document",
            new PlatformDocument { Settings = JsonElement.Parse(settingsJson) },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SettingsNotAnObjectDetail, await ReadProblemDetailAsync(response));
        var getResponse = await Client.GetAsync("/directory/v1/platform/document");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task PublishPlatformDocument_WhenCalledByAnotherCell_Returns403()
    {
        Authenticate();

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/platform/document",
            new PlatformDocument { Settings = JsonElement.Parse("""{"max_tenants":5}""") },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var getResponse = await Client.GetAsync("/directory/v1/platform/document");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
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
    public async Task RegisterCell_WhenTheRegionIsNotACellLabel_AnswersBadRequest()
    {
        Authenticate(clientId: "cell-a");

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/cells/a",
            CreateCell("a") with
            {
                Region = "West-Europe",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var listResponse = await Client.GetAsync("/directory/v1/cells");
        Assert.Empty(await ReadAsync<List<CellDescriptor>>(listResponse));
    }

    [Fact]
    public async Task RegisterCell_WhenTheKindIsOverMaxLength_AnswersBadRequest()
    {
        Authenticate(clientId: "cell-a");

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/cells/a",
            CreateCell("a") with
            {
                Kind = new string('k', DirectoryLimits.MaxCellKindLength + 1),
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CellTooLongDetail, await ReadProblemDetailAsync(response));
        var listResponse = await Client.GetAsync("/directory/v1/cells");
        Assert.Empty(await ReadAsync<List<CellDescriptor>>(listResponse));
    }

    [Fact]
    public async Task RegisterCell_WhenTheManagementUrlIsOverMaxLength_AnswersBadRequest()
    {
        Authenticate(clientId: "cell-a");

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/cells/a",
            CreateCell("a") with
            {
                ManagementUrl =
                    "https://a.example/" + new string('p', DirectoryLimits.MaxManagementUrlLength),
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CellTooLongDetail, await ReadProblemDetailAsync(response));
        var listResponse = await Client.GetAsync("/directory/v1/cells");
        Assert.Empty(await ReadAsync<List<CellDescriptor>>(listResponse));
    }

    [Fact]
    public async Task RegisterCell_WhenTheManagementUrlIsPlainHttp_AnswersBadRequest()
    {
        Authenticate(clientId: "cell-a");

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/cells/a",
            CreateCell("a") with
            {
                ManagementUrl = "http://a.example/",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvalidManagementUrlDetail, await ReadProblemDetailAsync(response));
        var listResponse = await Client.GetAsync("/directory/v1/cells");
        Assert.Empty(await ReadAsync<List<CellDescriptor>>(listResponse));
    }

    [Fact]
    public async Task RegisterCell_WhenTheManagementUrlIsHttpOnALoopbackHost_AnswersBadRequest()
    {
        Authenticate(clientId: "cell-a");

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/cells/a",
            CreateCell("a") with
            {
                ManagementUrl = "http://127.0.0.1:8080/",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvalidManagementUrlDetail, await ReadProblemDetailAsync(response));
        var listResponse = await Client.GetAsync("/directory/v1/cells");
        Assert.Empty(await ReadAsync<List<CellDescriptor>>(listResponse));
    }

    [Fact]
    public async Task RegisterCell_WhenLocalDevelopmentIsOn_RegistersAPlainHttpManagementUrl()
    {
        Authenticate(clientId: "cell-a");
        await using var app = await CreateAppAsync(serviceCollection =>
            serviceCollection.AddSingleton(new LocalDevelopmentMode(true))
        );
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var response = await client.PutAsJsonAsync(
            "/directory/v1/cells/a",
            CreateCell("a") with
            {
                ManagementUrl = "http://a.example/",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_WhenTheManagementUrlCarriesUserInformation_AnswersBadRequest()
    {
        Authenticate(clientId: "cell-a");

        var response = await Client.PutAsJsonAsync(
            "/directory/v1/cells/a",
            CreateCell("a") with
            {
                ManagementUrl = "https://u@a.example/",
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvalidManagementUrlDetail, await ReadProblemDetailAsync(response));
        var listResponse = await Client.GetAsync("/directory/v1/cells");
        Assert.Empty(await ReadAsync<List<CellDescriptor>>(listResponse));
    }

    [Fact]
    public async Task RegisterCell_WhenAnotherCellClaimsToBeTheControlPlane_AnswersForbidden()
    {
        Authenticate();

        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{CellName}",
            CreateCell(CellName) with
            {
                IsControlPlane = true,
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "Only the control-plane cell registers itself as the control plane.",
            await ReadProblemDetailAsync(response)
        );
        var listResponse = await Client.GetAsync("/directory/v1/cells");
        Assert.Empty(await ReadAsync<List<CellDescriptor>>(listResponse));
    }

    [Fact]
    public async Task RegisterCell_WhenTheControlPlaneRegistersItself_AnswersOk()
    {
        AuthenticateAsControlPlane();

        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{ControlPlaneCell}",
            CreateCell(ControlPlaneCell) with
            {
                IsControlPlane = true,
            },
            WebOptions
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCell_WhenNotRegistered_AnswersNotFound()
    {
        Authenticate();

        var response = await Client.GetAsync("/directory/v1/cells/a");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Route Metadata Tests

    [Fact]
    public void MapDirectoryEndpoints_WhenARouteBindsAJsonBody_DeclaresBadRequestAndUnsupportedMediaType()
    {
        var app = App ?? throw new InvalidOperationException();
        var dataSource = app.Services.GetRequiredService<EndpointDataSource>();

        var bodyEndpoints = dataSource
            .Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAcceptsMetadata>() is not null)
            .ToList();

        Assert.NotEmpty(bodyEndpoints);
        foreach (var endpoint in bodyEndpoints)
        {
            var statusCodes = endpoint
                .Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
                .Select(metadata => metadata.StatusCode)
                .ToHashSet();

            Assert.True(
                statusCodes.Contains(StatusCodes.Status400BadRequest),
                $"{endpoint.DisplayName} does not declare 400."
            );
            Assert.True(
                statusCodes.Contains(StatusCodes.Status415UnsupportedMediaType),
                $"{endpoint.DisplayName} does not declare 415."
            );
        }
    }

    [Fact]
    public void MapDirectoryEndpoints_MoveRoute_DeclaresBadRequestAndNotUnprocessableEntity()
    {
        var app = App ?? throw new InvalidOperationException();
        var dataSource = app.Services.GetRequiredService<EndpointDataSource>();

        var moveEndpoint = Assert.Single(
            dataSource.Endpoints.OfType<RouteEndpoint>(),
            endpoint =>
                endpoint.RoutePattern.RawText?.EndsWith("/move", StringComparison.Ordinal) == true
        );
        var statusCodes = moveEndpoint
            .Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToHashSet();

        Assert.Contains(StatusCodes.Status400BadRequest, statusCodes);
        Assert.DoesNotContain(StatusCodes.Status422UnprocessableEntity, statusCodes);
    }

    #endregion
}
