// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
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

/// <summary>
/// Runs the remote adapters of the global directory against the directory server, hosted in-process on a
/// <see cref="TestServer"/> over in-memory Sqlite, so that the adapters and the server agree on the wire contract.
/// </summary>
public class RemoteDirectoryRoundTripTests : IAsyncLifetime
{
    private const string ServerIssuer = "https://control-plane.example";
    private const string Audience = GlobalDirectoryProtocol.DirectoryAudience;
    private const string AccessToken = "directory-access-token";
    private const string CellName = "a";
    private const string OtherCellName = "b";
    private const string Region = "region-a";
    private const string TenantId = "tenant-1";
    private const string OtherTenantId = "tenant-2";
    private const string TenantIssuer = "https://op.example/tenant-1";
    private const string OtherTenantIssuer = "https://op.example/tenant-2";

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    private Mock<IIssuerAccessTokenValidator> ValidatorMock { get; }

    private SqliteConnection Connection { get; } = new("DataSource=:memory:");

    private WebApplication? App { get; set; }

    private HttpClient? HttpClientOrNull { get; set; }

    private ITenantDirectory? TenantsOrNull { get; set; }

    private ITeamDirectory? TeamsOrNull { get; set; }

    private IPlatformDirectory? PlatformOrNull { get; set; }

    private ICellDirectory? CellsOrNull { get; set; }

    private ITenantDirectory Tenants => TenantsOrNull ?? throw new InvalidOperationException();

    private ITeamDirectory Teams => TeamsOrNull ?? throw new InvalidOperationException();

    private IPlatformDirectory Platform => PlatformOrNull ?? throw new InvalidOperationException();

    private ICellDirectory Cells => CellsOrNull ?? throw new InvalidOperationException();

    public RemoteDirectoryRoundTripTests()
    {
        ValidatorMock = Mocks.Create<IIssuerAccessTokenValidator>();
    }

    public async Task InitializeAsync()
    {
        Connection.Open();

        ActAs(CellName);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DirectoryServer:Issuer"] = ServerIssuer,
                ["DirectoryServer:ControlPlaneCell"] = CellName,
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

        var httpClient = new HttpClient(app.GetTestServer().CreateHandler());
        HttpClientOrNull = httpClient;

        var factoryMock = Mocks.Create<IHttpClientFactory>();
        factoryMock
            .Setup(x => x.CreateClient(OpenIdClientDefaults.HttpClientName))
            .Returns(httpClient)
            .Verifiable(Times.AtLeastOnce);

        var tokenSourceMock = Mocks.Create<ICellTokenSource>();
        tokenSourceMock
            .Setup(x =>
                x.GetAccessTokenAsync(
                    GlobalDirectoryProtocol.DirectoryAudience,
                    GlobalDirectoryProtocol.DirectoryScope,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<string>(AccessToken))
            .Verifiable(Times.AtLeastOnce);

        var client = new DirectoryHttpClient(
            factoryMock.Object,
            tokenSourceMock.Object,
            Microsoft.Extensions.Options.Options.Create(
                new DirectoryClientOptions { Url = "http://localhost/" }
            )
        );

        TenantsOrNull = new RemoteTenantDirectory(client);
        TeamsOrNull = new RemoteTeamDirectory(client);
        PlatformOrNull = new RemotePlatformDirectory(client);
        CellsOrNull = new RemoteCellDirectory(client);
    }

    public async Task DisposeAsync()
    {
        HttpClientOrNull?.Dispose();
        if (App is not null)
            await App.DisposeAsync();

        await Connection.DisposeAsync();
        Mocks.Verify();
    }

    /// <summary>
    /// Makes the access token that the remote adapters send authenticate as the machine client of a cell.
    /// </summary>
    private void ActAs(string cellName) =>
        ValidatorMock
            .Setup(x =>
                x.ValidateAsync(ServerIssuer, Audience, AccessToken, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                CreateValidResult(
                    GlobalDirectoryProtocol.CellClientIdPrefix + cellName,
                    GlobalDirectoryProtocol.DirectoryScope
                )
            )
            .Verifiable(Times.AtLeastOnce);

    private static IssuerAccessTokenResult CreateValidResult(string clientId, string scope)
    {
        var payload = JsonElement.Parse(
            JsonSerializer.Serialize(new { client_id = clientId, scope })
        );
        return new IssuerAccessTokenResult(payload, ServerIssuer);
    }

    private static string SettingsWithLimit(int maxTenants) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["max_tenants"] = maxTenants });

    private static TenantReservation CreateReservation(
        string tenantId,
        string issuer,
        string cell = CellName,
        string? teamId = null
    ) =>
        new()
        {
            TenantId = tenantId,
            Issuer = issuer,
            Cell = cell,
            TeamId = teamId,
        };

    private static TeamDocument CreateTeam(
        string teamId,
        string? parentTeamId = null,
        string settingsJson = "{}"
    ) =>
        new()
        {
            TeamId = teamId,
            DisplayName = $"Team {teamId}",
            IsDisabled = false,
            ParentTeamId = parentTeamId,
            SettingsJson = settingsJson,
            RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty.Add(
                "default",
                new TeamRootKeyReference("aws-kms", "key-reference")
                {
                    AccessIdentity = "access-identity",
                }
            ),
        };

    #region Tenant Tests

    [Fact]
    public async Task ReserveAsync_WhenTheIdentifierAndIssuerAreFree_ReservesTheTenant()
    {
        var result = await Tenants.ReserveAsync(
            CreateReservation(TenantId, TenantIssuer),
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
        Assert.Null(result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenAnotherCellHoldsTheIdentifier_ConflictsWithTheHeldIdentifierText()
    {
        Assert.True(
            (
                await Tenants.ReserveAsync(
                    CreateReservation(TenantId, TenantIssuer),
                    CancellationToken.None
                )
            ).IsReserved
        );

        ActAs(OtherCellName);
        var result = await Tenants.ReserveAsync(
            CreateReservation(TenantId, OtherTenantIssuer, OtherCellName),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"Another tenant already has the identifier '{TenantId}'.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenAnotherCellHoldsTheIssuer_ConflictsWithTheHeldIssuerText()
    {
        Assert.True(
            (
                await Tenants.ReserveAsync(
                    CreateReservation(TenantId, TenantIssuer),
                    CancellationToken.None
                )
            ).IsReserved
        );

        ActAs(OtherCellName);
        var result = await Tenants.ReserveAsync(
            CreateReservation(OtherTenantId, TenantIssuer, OtherCellName),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"Another tenant already has the issuer '{TenantIssuer}'.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task PlaceAsync_WhenTheTenantIsReserved_AnswersThePlacement()
    {
        await Cells.RegisterAsync(
            new CellDescriptor
            {
                Name = CellName,
                Region = Region,
                Kind = CellKinds.Shared,
                ManagementUrl = "https://a.example/",
            },
            CancellationToken.None
        );
        Assert.True(
            (
                await Tenants.ReserveAsync(
                    CreateReservation(TenantId, TenantIssuer),
                    CancellationToken.None
                )
            ).IsReserved
        );
        Assert.Null(await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None));

        var placeResult = await Tenants.PlaceAsync(
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = TenantIssuer,
                Cell = CellName,
                Region = Region,
            },
            CancellationToken.None
        );

        Assert.True(placeResult.IsReserved);
        var placement = await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None);
        Assert.NotNull(placement);
        Assert.Equal(TenantId, placement.TenantId);
        Assert.Equal(TenantIssuer, placement.Issuer);
        Assert.Equal(CellName, placement.Cell);
        Assert.Equal(Region, placement.Region);
    }

    [Fact]
    public async Task ReleaseAsync_WhenTheTenantIsReserved_FreesTheIdentifier()
    {
        Assert.True(
            (
                await Tenants.ReserveAsync(
                    CreateReservation(TenantId, TenantIssuer),
                    CancellationToken.None
                )
            ).IsReserved
        );

        await Tenants.ReleaseAsync(TenantId, CancellationToken.None);

        ActAs(OtherCellName);
        var result = await Tenants.ReserveAsync(
            CreateReservation(TenantId, TenantIssuer, OtherCellName),
            CancellationToken.None
        );
        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task RetireAsync_WhenTheTenantIsRetired_RecordsTheIdentifierAndTheIssuer()
    {
        Assert.False(await Tenants.IsTenantIdRetiredAsync(TenantId, CancellationToken.None));
        Assert.Null(
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(TenantIssuer, CancellationToken.None)
        );
        Assert.True(
            (
                await Tenants.ReserveAsync(
                    CreateReservation(TenantId, TenantIssuer),
                    CancellationToken.None
                )
            ).IsReserved
        );

        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = TenantIssuer },
            CancellationToken.None
        );

        Assert.True(await Tenants.IsTenantIdRetiredAsync(TenantId, CancellationToken.None));
        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(TenantIssuer, CancellationToken.None)
        );
        Assert.Null(await Tenants.GetPlacementOrDefaultAsync(TenantId, CancellationToken.None));
    }

    [Fact]
    public async Task ReserveAsync_WhenTheIdentifierIsRetired_ConflictsWithTheDeletedTenantText()
    {
        await Tenants.RetireAsync(
            new TenantRetirement { TenantId = TenantId, Issuer = TenantIssuer },
            CancellationToken.None
        );

        var result = await Tenants.ReserveAsync(
            CreateReservation(TenantId, OtherTenantIssuer),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"The tenant identifier '{TenantId}' belonged to a deleted tenant and is never reissued.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheNewIssuerIsFree_RetiresThePreviousIssuer()
    {
        Assert.True(
            (
                await Tenants.ReserveAsync(
                    CreateReservation(TenantId, TenantIssuer),
                    CancellationToken.None
                )
            ).IsReserved
        );

        var result = await Tenants.ChangeIssuerAsync(
            new TenantIssuerChange
            {
                TenantId = TenantId,
                PreviousIssuer = TenantIssuer,
                Issuer = OtherTenantIssuer,
            },
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
        Assert.Equal(
            TenantId,
            await Tenants.GetRetiredIssuerHolderOrDefaultAsync(TenantIssuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenAnotherTenantHoldsTheIssuer_ConflictsWithTheHeldIssuerText()
    {
        Assert.True(
            (
                await Tenants.ReserveAsync(
                    CreateReservation(TenantId, TenantIssuer),
                    CancellationToken.None
                )
            ).IsReserved
        );
        Assert.True(
            (
                await Tenants.ReserveAsync(
                    CreateReservation(OtherTenantId, OtherTenantIssuer),
                    CancellationToken.None
                )
            ).IsReserved
        );

        var result = await Tenants.ChangeIssuerAsync(
            new TenantIssuerChange
            {
                TenantId = TenantId,
                PreviousIssuer = TenantIssuer,
                Issuer = OtherTenantIssuer,
            },
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal(
            $"Another tenant already has the issuer '{OtherTenantIssuer}'.",
            result.ConflictDetail
        );
    }

    #endregion

    #region Team And Platform Tests

    [Fact]
    public async Task PublishAsync_WhenATeamIsPublished_AnswersTheDocumentWithItsSequence()
    {
        Assert.Null(await Teams.GetOrDefaultAsync("team-1", CancellationToken.None));

        await Teams.PublishAsync(
            CreateTeam("team-1", settingsJson: SettingsWithLimit(3)),
            CancellationToken.None
        );

        var document = await Teams.GetOrDefaultAsync("team-1", CancellationToken.None);
        Assert.NotNull(document);
        Assert.Equal("team-1", document.TeamId);
        Assert.Equal("Team team-1", document.DisplayName);
        Assert.Null(document.ParentTeamId);
        Assert.Equal(SettingsWithLimit(3), document.SettingsJson);
        var rootKey = document.RootKeys["default"];
        Assert.Equal("aws-kms", rootKey.KeyWrapperName);
        Assert.Equal("key-reference", rootKey.KeyReference);
        Assert.Equal("access-identity", rootKey.AccessIdentity);
        Assert.Equal(1, document.Sequence);
    }

    [Fact]
    public async Task RetireAsync_WhenATeamIsRetired_ReportsTheIdentifierAsRetired()
    {
        Assert.False(await Teams.IsTeamIdRetiredAsync("team-1", CancellationToken.None));
        await Teams.PublishAsync(CreateTeam("team-1"), CancellationToken.None);

        await Teams.RetireAsync("team-1", CancellationToken.None);

        Assert.True(await Teams.IsTeamIdRetiredAsync("team-1", CancellationToken.None));
        Assert.Null(await Teams.GetOrDefaultAsync("team-1", CancellationToken.None));
    }

    [Fact]
    public async Task ReserveAsync_WhenATeamIsReservedTwice_ConflictsTheSecondTime()
    {
        Assert.True((await Teams.ReserveAsync("team-1", CancellationToken.None)).IsReserved);

        var result = await Teams.ReserveAsync("team-1", CancellationToken.None);

        Assert.False(result.IsReserved);
        Assert.False(string.IsNullOrEmpty(result.ConflictDetail));
    }

    [Fact]
    public async Task PublishAsync_WhenThePlatformDocumentIsPublished_AnswersTheDocument()
    {
        Assert.Null(await Platform.GetOrDefaultAsync(CancellationToken.None));

        await Platform.PublishAsync(
            new PlatformDocument { SettingsJson = SettingsWithLimit(5) },
            CancellationToken.None
        );

        var document = await Platform.GetOrDefaultAsync(CancellationToken.None);
        Assert.NotNull(document);
        Assert.Equal(SettingsWithLimit(5), document.SettingsJson);
        Assert.Equal(1, document.Sequence);
    }

    #endregion

    #region Limit Tests

    [Fact]
    public async Task ReserveAsync_WhenThePlatformIsAtItsLimit_ConflictsWithTheTeamLimitText()
    {
        await Platform.PublishAsync(
            new PlatformDocument { SettingsJson = SettingsWithLimit(1) },
            CancellationToken.None
        );
        await Teams.PublishAsync(CreateTeam("team-1"), CancellationToken.None);
        Assert.True(
            (
                await Tenants.ReserveAsync(
                    CreateReservation(TenantId, TenantIssuer, teamId: "team-1"),
                    CancellationToken.None
                )
            ).IsReserved
        );

        var result = await Tenants.ReserveAsync(
            CreateReservation(OtherTenantId, OtherTenantIssuer, teamId: "team-1"),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'team-1' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheParentTeamIsAtItsLimit_ConflictsWithTheParentTeamText()
    {
        await Teams.PublishAsync(
            CreateTeam("parent", settingsJson: SettingsWithLimit(1)),
            CancellationToken.None
        );
        await Teams.PublishAsync(
            CreateTeam("child", parentTeamId: "parent"),
            CancellationToken.None
        );
        Assert.True(
            (
                await Tenants.ReserveAsync(
                    CreateReservation(TenantId, TenantIssuer, teamId: "child"),
                    CancellationToken.None
                )
            ).IsReserved
        );

        var result = await Tenants.ReserveAsync(
            CreateReservation(OtherTenantId, OtherTenantIssuer, teamId: "child"),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'parent' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    #endregion

    #region Change Tests

    [Fact]
    public async Task ListChangesAsync_WithTeamAndPlatformChanges_PagesTheChangesByAfter()
    {
        await Teams.PublishAsync(CreateTeam("team-1"), CancellationToken.None);
        await Teams.PublishAsync(CreateTeam("team-2"), CancellationToken.None);
        await Platform.PublishAsync(
            new PlatformDocument { SettingsJson = SettingsWithLimit(5) },
            CancellationToken.None
        );
        await Teams.RetireAsync("team-2", CancellationToken.None);

        var firstPage = await Teams.ListChangesAsync(0, 2, CancellationToken.None);
        var secondPage = await Teams.ListChangesAsync(
            firstPage[^1].Sequence,
            10,
            CancellationToken.None
        );

        Assert.Equal(2, firstPage.Count);
        var teamChange = firstPage[0];
        Assert.Equal("team-1", teamChange.TeamId);
        Assert.NotNull(teamChange.Document);
        Assert.Equal("Team team-1", teamChange.Document.DisplayName);
        Assert.Null(teamChange.Platform);
        var platformChange = firstPage[1];
        Assert.Null(platformChange.TeamId);
        Assert.Null(platformChange.Document);
        Assert.NotNull(platformChange.Platform);
        Assert.Equal(SettingsWithLimit(5), platformChange.Platform.SettingsJson);
        Assert.True(platformChange.Sequence > teamChange.Sequence);
        var retirement = Assert.Single(secondPage);
        Assert.Equal("team-2", retirement.TeamId);
        Assert.Null(retirement.Document);
        Assert.Null(retirement.Platform);
        Assert.True(retirement.Sequence > platformChange.Sequence);
    }

    #endregion

    #region Cell Tests

    [Fact]
    public async Task RegisterAsync_WhenTheCallerIsTheCell_RegistersItAndListsIt()
    {
        Assert.Null(await Cells.GetOrDefaultAsync(CellName, CancellationToken.None));

        await Cells.RegisterAsync(
            new CellDescriptor
            {
                Name = CellName,
                Region = Region,
                Kind = CellKinds.Dedicated,
                ManagementUrl = "https://a.example/",
            },
            CancellationToken.None
        );

        var cell = await Cells.GetOrDefaultAsync(CellName, CancellationToken.None);
        Assert.NotNull(cell);
        Assert.Equal(Region, cell.Region);
        Assert.Equal(CellKinds.Dedicated, cell.Kind);
        Assert.Equal("https://a.example/", cell.ManagementUrl);
        var listed = Assert.Single(await Cells.ListAsync(CancellationToken.None));
        Assert.Equal(CellName, listed.Name);
    }

    #endregion
}
