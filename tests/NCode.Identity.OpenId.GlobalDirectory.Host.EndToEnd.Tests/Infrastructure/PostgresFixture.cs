// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.EndToEnd.Tests.Infrastructure;

/// <summary>
/// The Postgres server that the end-to-end tests share: a <c>postgres:18</c> container with the schema owner
/// <see cref="OwnerRole"/> and the runtime role <see cref="RuntimeRole"/>, which logs in and owns nothing. Each test
/// asks for an empty database that the owner owns, so a test never sees another test's schema.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Image = "postgres:18";

    /// <summary>
    /// The name of the schema owner, which owns the databases the tests migrate.
    /// </summary>
    public const string OwnerRole = "directory_owner";

    /// <summary>
    /// The name of the runtime role, which the directory's replicas connect as and which owns nothing.
    /// </summary>
    public const string RuntimeRole = "directory_app";

    private PostgreSqlContainer? ContainerOrNull { get; set; }
    private string? AdministratorConnectionStringOrNull { get; set; }
    private string? OwnerPasswordOrNull { get; set; }

    private string AdministratorConnectionString =>
        AdministratorConnectionStringOrNull ?? throw NotInitialized();

    private string OwnerPassword => OwnerPasswordOrNull ?? throw NotInitialized();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var container = new PostgreSqlBuilder(Image).Build();
        ContainerOrNull = container;
        await container.StartAsync();

        var administratorConnectionString = container.GetConnectionString();
        var ownerPassword = CreatePassword();
        var runtimePassword = CreatePassword();

        // A role statement takes no parameters; each password is hexadecimal, so it is safe to quote as a literal.
        await ExecuteAsync(
            administratorConnectionString,
            $"CREATE ROLE {OwnerRole} LOGIN NOSUPERUSER PASSWORD '{ownerPassword}'",
            CancellationToken.None
        );
        await ExecuteAsync(
            administratorConnectionString,
            $"CREATE ROLE {RuntimeRole} LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{runtimePassword}'",
            CancellationToken.None
        );

        AdministratorConnectionStringOrNull = administratorConnectionString;
        OwnerPasswordOrNull = ownerPassword;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        if (ContainerOrNull is not null)
            await ContainerOrNull.DisposeAsync();
    }

    /// <summary>
    /// Creates an empty database that <see cref="OwnerRole"/> owns.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The connection string of the schema owner to the new database.</returns>
    public async ValueTask<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var name = $"directory_{Guid.NewGuid():N}";

        // The name is hexadecimal, so it is safe to quote as an identifier.
        await ExecuteAsync(
            AdministratorConnectionString,
            $"CREATE DATABASE \"{name}\" OWNER {OwnerRole}",
            cancellationToken
        );

        return new NpgsqlConnectionStringBuilder(AdministratorConnectionString)
        {
            Database = name,
            Username = OwnerRole,
            Password = OwnerPassword,
        }.ConnectionString;
    }

    private static string CreatePassword() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private static async ValueTask ExecuteAsync(
        string connectionString,
        string statement,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static InvalidOperationException NotInitialized() =>
        new("The Postgres fixture is not initialized.");
}
