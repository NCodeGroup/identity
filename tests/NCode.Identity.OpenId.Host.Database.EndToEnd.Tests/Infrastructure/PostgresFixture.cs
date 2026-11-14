// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using IdGen;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.EntityFramework;
using NCode.Identity.OpenId.Persistence.EntityFramework.Accounts;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace NCode.Identity.OpenId.Host.Database.EndToEnd.Tests.Infrastructure;

/// <summary>
/// The Postgres database that the end-to-end tests share: a <c>postgres:18</c> container that the schema owner
/// migrates, with row-level security applied for the runtime role <see cref="RuntimeRole"/>, which is
/// <c>NOSUPERUSER NOBYPASSRLS</c>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Image = "postgres:18";

    /// <summary>
    /// The name of the runtime role, which the host connects as and which row-level security applies to.
    /// </summary>
    public const string RuntimeRole = "app";

    private PostgreSqlContainer? ContainerOrNull { get; set; }
    private ServiceProvider? ServiceProviderOrNull { get; set; }
    private string? OwnerConnectionStringOrNull { get; set; }
    private string? RuntimeConnectionStringOrNull { get; set; }

    /// <summary>
    /// Gets the connection string of the schema owner, which owns the tables and is a superuser.
    /// </summary>
    public string OwnerConnectionString => OwnerConnectionStringOrNull ?? throw NotInitialized();

    /// <summary>
    /// Gets the connection string of the runtime role <see cref="RuntimeRole"/>.
    /// </summary>
    public string RuntimeConnectionString =>
        RuntimeConnectionStringOrNull ?? throw NotInitialized();

    /// <summary>
    /// Gets a provider that has the OpenID persistence slice registered, which constructing an
    /// <see cref="OpenIdDbContext"/> needs.
    /// </summary>
    public IServiceProvider ServiceProvider => ServiceProviderOrNull ?? throw NotInitialized();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var container = new PostgreSqlBuilder(Image).Build();
        ContainerOrNull = container;
        await container.StartAsync();

        var ownerConnectionString = container.GetConnectionString();
        var runtimePassword = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        await CreateRuntimeRoleAsync(
            ownerConnectionString,
            runtimePassword,
            CancellationToken.None
        );

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IIdGenerator<long>>(new IdGenerator(0))
            .AddEntityFrameworkCorePersistence<OpenIdDbContext>()
            .AddLocalAccountEntityFramework()
            .ServiceCollection.BuildServiceProvider();
        ServiceProviderOrNull = serviceProvider;

        await MigrationRunner.MigrateAllAsync(
            serviceProvider,
            ownerConnectionString,
            RuntimeRole,
            CancellationToken.None
        );

        OwnerConnectionStringOrNull = ownerConnectionString;
        RuntimeConnectionStringOrNull = new NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = RuntimeRole,
            Password = runtimePassword,
        }.ConnectionString;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (ServiceProviderOrNull is not null)
            await ServiceProviderOrNull.DisposeAsync();

        NpgsqlConnection.ClearAllPools();

        if (ContainerOrNull is not null)
            await ContainerOrNull.DisposeAsync();
    }

    private static async ValueTask CreateRuntimeRoleAsync(
        string connectionString,
        string password,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // A role statement takes no parameters; the password is hexadecimal, so it is safe to quote as a literal.
        await using var command = new NpgsqlCommand(
            $"CREATE ROLE {RuntimeRole} LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{password}'",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static InvalidOperationException NotInitialized() =>
        new("The Postgres fixture is not initialized.");
}
