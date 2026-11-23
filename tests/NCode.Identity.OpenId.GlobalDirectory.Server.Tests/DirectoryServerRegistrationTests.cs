// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Client;
using NCode.Identity.Registration;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DirectoryServerRegistrationTests
{
    private const string ServerIssuer = "https://control-plane.example";
    private const string ControlPlaneCell = "control";
    private const string ControlPlaneTenantId = "root";

    private static WebApplication CreateApp(
        SqliteConnection connection,
        IReadOnlyDictionary<string, string?> values
    )
    {
        var validatorMock = new Mock<IIssuerAccessTokenValidator>(MockBehavior.Strict);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddDbContextFactory<DirectoryDbContext>(options =>
            options.UseSqlite(connection)
        );
        builder.Services.AddSingleton(validatorMock.Object);
        new ServiceBuilder<OpenIdClientLibrary>(builder.Services).AddOpenIdDirectoryServer(
            builder.Configuration
        );

        var app = builder.Build();
        app.MapDirectory();
        return app;
    }

    #region AddOpenIdDirectoryServer Tests

    [Fact]
    public async Task AddOpenIdDirectoryServer_WhenCalled_RegistersThePayloadSerializer()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await using var app = CreateApp(
            connection,
            new Dictionary<string, string?> { ["AllowedHosts"] = "localhost" }
        );

        Assert.NotNull(app.Services.GetService<IPersistedPayloadSerializer>());
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WhenTheDirectoryServerSectionIsMissing_ThrowsOptionsValidationException()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await using var app = CreateApp(
            connection,
            new Dictionary<string, string?> { ["AllowedHosts"] = "localhost" }
        );

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            app.StartAsync()
        );

        Assert.Contains("DirectoryServer:Issuer", exception.Message);
        Assert.Contains("DirectoryServer:ControlPlaneCell", exception.Message);
        Assert.Contains("DirectoryServer:ControlPlaneTenantId", exception.Message);
    }

    [Fact]
    public async Task StartAsync_WhenTheOptionsAreValid_Starts()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await using var app = CreateApp(
            connection,
            new Dictionary<string, string?>
            {
                ["DirectoryServer:Issuer"] = ServerIssuer,
                ["DirectoryServer:ControlPlaneCell"] = ControlPlaneCell,
                ["DirectoryServer:ControlPlaneTenantId"] = ControlPlaneTenantId,
                ["AllowedHosts"] = "localhost",
            }
        );

        await app.StartAsync();
        await app.StopAsync();
    }

    #endregion
}
