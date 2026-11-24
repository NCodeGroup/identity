// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using NCode.Identity.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DirectoryAnchorInitializerTests : IDisposable
{
    private const string ControlPlaneCell = "control";
    private const string ControlPlaneTenantId = "root";
    private const string ControlPlaneIssuer = "https://op.example/root";
    private const string ClientJwks = """{"keys":[]}""";

    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }
    private IDbContextFactory<DirectoryDbContext> DbContextFactory { get; }
    private DirectoryAnchorState State { get; } = new();

    public DirectoryAnchorInitializerTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDbContextFactory<DirectoryDbContext>(options =>
            options.UseSqlite(Connection)
        );
        serviceCollection.AddSurrogateKeyGenerator();
        Provider = serviceCollection.BuildServiceProvider();
        DbContextFactory = Provider.GetRequiredService<IDbContextFactory<DirectoryDbContext>>();

        using var dbContext = DbContextFactory.CreateDbContext();
        dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Provider.Dispose();
        Connection.Dispose();
    }

    private DirectoryAnchorInitializer CreateInitializer() =>
        new(
            DbContextFactory,
            Microsoft.Extensions.Options.Options.Create(
                new DirectoryServerOptions
                {
                    Issuer = "https://directory.example",
                    ControlPlane = new DirectoryControlPlaneOptions
                    {
                        Cell = ControlPlaneCell,
                        Region = "region-1",
                        Kind = CellKinds.Shared,
                        TenantId = ControlPlaneTenantId,
                        Issuer = ControlPlaneIssuer,
                        ClientJwks = ClientJwks,
                    },
                }
            ),
            Provider.GetRequiredService<ISurrogateKeyGenerator>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
            State
        );

    [Fact]
    public async Task StartAsync_WhenTheDatabaseIsEmpty_WritesTheAnchorAndMarksItInitialized()
    {
        Assert.False(State.IsInitialized);

        await CreateInitializer().StartAsync(CancellationToken.None);

        Assert.True(State.IsInitialized);
        await using var dbContext = await DbContextFactory.CreateDbContextAsync();
        var cell = await dbContext.Cells.AsNoTracking().SingleAsync();
        Assert.Equal(ControlPlaneCell, cell.Name);
        Assert.Equal("region-1", cell.Region);
        Assert.Equal(CellKinds.Shared, cell.Kind);
        Assert.Equal(ClientJwks, cell.ClientJwksJson);
        Assert.True(cell.IsControlPlane);
        Assert.False(cell.IsRetired);
        var tenant = await dbContext.Tenants.AsNoTracking().SingleAsync();
        Assert.Equal(ControlPlaneTenantId, tenant.TenantId);
        Assert.Equal(ControlPlaneIssuer, tenant.Issuer);
        Assert.Equal(ControlPlaneCell, tenant.Cell);
        Assert.Equal(TenantRecord.StatePlaced, tenant.State);
    }

    [Fact]
    public async Task StartAsync_WhenCalledTwice_ChangesNothingTheSecondTime()
    {
        await CreateInitializer().StartAsync(CancellationToken.None);
        await CreateInitializer().StartAsync(CancellationToken.None);

        await using var dbContext = await DbContextFactory.CreateDbContextAsync();
        Assert.Equal(1, await dbContext.Cells.CountAsync());
        Assert.Equal(1, await dbContext.Tenants.CountAsync());
        Assert.True(State.IsInitialized);
    }

    [Fact]
    public async Task StartAsync_WhenAnotherCellHoldsTheTenant_ThrowsAndStaysUninitialized()
    {
        await using (var dbContext = await DbContextFactory.CreateDbContextAsync())
        {
            dbContext.Tenants.Add(
                new TenantRecord
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = ControlPlaneTenantId,
                    NormalizedTenantId = ControlPlaneTenantId,
                    Issuer = "https://op.example/other",
                    NormalizedIssuer = "https://op.example/other",
                    Cell = "other",
                    State = TenantRecord.StatePlaced,
                }
            );
            await dbContext.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateInitializer().StartAsync(CancellationToken.None)
        );

        Assert.False(State.IsInitialized);
    }

    [Fact]
    public async Task StartAsync_WhenTheCellIsRetired_ThrowsAndStaysUninitialized()
    {
        await using (var dbContext = await DbContextFactory.CreateDbContextAsync())
        {
            dbContext.Cells.Add(
                new CellRecord
                {
                    Name = ControlPlaneCell,
                    Region = "region-1",
                    Kind = CellKinds.Shared,
                    IsRetired = true,
                }
            );
            await dbContext.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateInitializer().StartAsync(CancellationToken.None)
        );

        Assert.False(State.IsInitialized);
    }

    [Fact]
    public async Task InvokeAsync_BeforeTheAnchorIsInitialized_AnswersServiceUnavailable()
    {
        var gate = new DirectoryAnchorGate(State);
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };

        var result = await gate.InvokeAsync(httpContext, CancellationToken.None);

        Assert.NotNull(result);
        await result.ExecuteAsync(httpContext);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
        Assert.True(httpContext.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task InvokeAsync_AfterTheAnchorIsInitialized_LetsTheRouteRun()
    {
        State.MarkInitialized();
        var gate = new DirectoryAnchorGate(State);

        var result = await gate.InvokeAsync(new DefaultHttpContext(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckHealthAsync_ReportsTheAnchorState()
    {
        var healthCheck = new DirectoryAnchorHealthCheck(State);
        var context = new HealthCheckContext();

        var before = await healthCheck.CheckHealthAsync(context);
        State.MarkInitialized();
        var after = await healthCheck.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Unhealthy, before.Status);
        Assert.Equal(HealthStatus.Healthy, after.Status);
    }
}
