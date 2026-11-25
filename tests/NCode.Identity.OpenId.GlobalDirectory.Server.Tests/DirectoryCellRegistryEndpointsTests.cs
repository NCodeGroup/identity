// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
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
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryCellRegistryEndpointsTests : IAsyncLifetime
{
    private const string ServerIssuer = "https://control-plane.example";
    private const string ControlPlaneCell = "control";
    private const string CellName = "a";
    private const string Region = "region-1";

    private static JsonSerializerOptions WebOptions { get; } = new(JsonSerializerDefaults.Web);

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
        builder.Services.AddSingleton<IPublicJwksReader, RefusingEmptyJwksReader>();
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

    private static object CreateBody(
        string region = Region,
        string kind = CellKinds.Shared,
        string jwks = """{"keys":[{"kty":"oct"}]}"""
    ) =>
        new
        {
            region,
            kind,
            jwks = JsonElement.Parse(jwks),
        };

    private static CellDescriptor CreateDescriptor(
        string name = CellName,
        string region = Region,
        string kind = CellKinds.Shared,
        string? managementUrl = null
    ) =>
        new()
        {
            Name = name,
            Region = region,
            Kind = kind,
            ManagementUrl = managementUrl ?? $"https://{name}.example/",
        };

    private async ValueTask<HttpResponseMessage> RegisterAsync(
        string name = CellName,
        object? body = null,
        CancellationToken cancellationToken = default
    )
    {
        await AuthenticateAsync(ControlPlaneCell, cancellationToken);
        return await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{name}/registration",
            body ?? CreateBody(),
            WebOptions,
            cancellationToken
        );
    }

    private async ValueTask<HttpResponseMessage> ReportAsync(
        CellDescriptor descriptor,
        CancellationToken cancellationToken = default
    )
    {
        await AuthenticateAsync(descriptor.Name, cancellationToken);
        return await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{descriptor.Name}",
            descriptor,
            WebOptions,
            cancellationToken
        );
    }

    [Fact]
    public async Task RegisterCell_FromTheControlPlane_AnswersNoContent()
    {
        var response = await RegisterAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_AgainWithAnotherKeySet_AnswersNoContent()
    {
        await RegisterAsync();

        var response = await RegisterAsync(
            body: CreateBody(jwks: """{"keys":[{"kty":"oct","kid":"2"}]}""")
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_FromAnotherCell_AnswersForbidden()
    {
        await AuthenticateAsync("b");

        var response = await Client.PutAsJsonAsync(
            $"/directory/v1/cells/{CellName}/registration",
            CreateBody(),
            WebOptions
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_WhenTheRegionIsNotACellLabel_AnswersBadRequest()
    {
        var response = await RegisterAsync(body: CreateBody(region: "West Europe"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_WhenTheNameIsNotACellLabel_AnswersBadRequest()
    {
        var response = await RegisterAsync(name: "Bad_Name");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_WhenTheKindIsBlank_AnswersBadRequest()
    {
        var response = await RegisterAsync(body: CreateBody(kind: " "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_WhenTheKeySetIsUnreadable_AnswersBadRequest()
    {
        var response = await RegisterAsync(body: CreateBody(jwks: """{"keys":[{"kty":"bad"}]}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_WhenTheKeySetIsMissing_AnswersBadRequest()
    {
        var response = await RegisterAsync(body: new { region = Region, kind = CellKinds.Shared });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_WhenTheNameIsTheControlPlaneCell_AnswersConflict()
    {
        var response = await RegisterAsync(name: ControlPlaneCell);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_WhenTheRegionChanges_AnswersConflict()
    {
        await RegisterAsync();

        var response = await RegisterAsync(body: CreateBody(region: "region-2"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCell_WhenTheKindChanges_AnswersConflict()
    {
        await RegisterAsync();

        var response = await RegisterAsync(body: CreateBody(kind: "other"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RetireCell_FromTheControlPlane_AnswersNoContentAndTheNameIsNeverReissued()
    {
        await RegisterAsync();

        var response = await Client.DeleteAsync($"/directory/v1/cells/{CellName}");
        var again = await RegisterAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task RetireCell_FromAnotherCell_AnswersForbidden()
    {
        await RegisterAsync();
        await AuthenticateAsync("b");

        var response = await Client.DeleteAsync($"/directory/v1/cells/{CellName}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RetireCell_WhenTheCellIsNotRegistered_AnswersConflict()
    {
        await AuthenticateAsync(ControlPlaneCell);

        var response = await Client.DeleteAsync($"/directory/v1/cells/{CellName}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RetireCell_WhenTheCellIsTheControlPlaneCell_AnswersConflict()
    {
        await AuthenticateAsync(ControlPlaneCell);

        var response = await Client.DeleteAsync($"/directory/v1/cells/{ControlPlaneCell}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RetireCell_WhenTheNameIsNotACellLabel_AnswersBadRequest()
    {
        await AuthenticateAsync(ControlPlaneCell);

        var response = await Client.DeleteAsync("/directory/v1/cells/Bad_Name");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RetireCell_WhileItHoldsATenant_AnswersConflict()
    {
        await RegisterAsync();
        await ReportAsync(CreateDescriptor());
        var reserved = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/reserve",
            new TenantReservation { TenantId = "tenant-1", Cell = CellName },
            WebOptions
        );
        var placed = await Client.PostAsJsonAsync(
            "/directory/v1/tenants/place",
            new TenantPlacementRequest { TenantId = "tenant-1", Cell = CellName },
            WebOptions
        );
        await AuthenticateAsync(ControlPlaneCell);

        var response = await Client.DeleteAsync($"/directory/v1/cells/{CellName}");

        Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReportCell_AfterItsRegistration_AnswersOkAndListsIt()
    {
        await RegisterAsync();

        var response = await ReportAsync(CreateDescriptor());
        var list = await Client.GetFromJsonAsync<List<CellDescriptor>>(
            "/directory/v1/cells",
            WebOptions
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(list ?? [], cell => cell.Name == CellName);
    }

    [Fact]
    public async Task ReportCell_WithoutARegistration_AnswersConflict()
    {
        var response = await ReportAsync(CreateDescriptor());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReportCell_WithAnotherRegion_AnswersConflict()
    {
        await RegisterAsync();

        var response = await ReportAsync(CreateDescriptor(region: "region-2"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReportCell_WithAnotherKind_AnswersConflict()
    {
        await RegisterAsync();

        var response = await ReportAsync(CreateDescriptor(kind: "other"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReportCell_WithAManagementUrlAnotherCellReports_AnswersConflict()
    {
        await RegisterAsync();
        await RegisterAsync("b");
        await ReportAsync(CreateDescriptor());

        var response = await ReportAsync(
            CreateDescriptor("b", managementUrl: "https://a.example/")
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReportCell_OfARetiredCell_AnswersConflict()
    {
        await RegisterAsync();
        await Client.DeleteAsync($"/directory/v1/cells/{CellName}");

        var response = await ReportAsync(CreateDescriptor());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// Reads every key set except one that holds a key of the type <c>bad</c>.
    /// </summary>
    private sealed class RefusingEmptyJwksReader : IPublicJwksReader
    {
        public bool TryRead(
            JsonElement jwks,
            [NotNullWhen(true)] out IReadOnlyList<SecretKey>? keys,
            [NotNullWhen(false)] out string? error
        )
        {
            if (!jwks.GetRawText().Contains("\"bad\"", StringComparison.Ordinal))
            {
                keys = [];
                error = null;
                return true;
            }

            keys = null;
            error = "The key set holds a bad key.";
            return false;
        }
    }
}
