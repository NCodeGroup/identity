// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NCode.Identity.OpenId.GlobalDirectory.Server;
using NCode.Identity.Registration;

namespace NCode.Identity.OpenId.GlobalDirectory.Host;

/// <summary>
/// Npgsql wiring for the directory's own database. The host and the EF Core design-time factory configure the database
/// through these helpers, so the provider and the migrations assembly have one source of truth.
/// </summary>
/// <remarks>
/// The directory's replicas connect through the <c>directory</c> connection string as a runtime role that owns nothing.
/// The <c>migrate</c> command connects through the <c>directory-migration</c> connection string as the role that owns
/// the schema, applies the migrations and grants the runtime role, named by <c>DirectoryDatabase:RuntimeRole</c>, the
/// data access it needs.
/// </remarks>
internal static class DirectoryDatabase
{
    /// <summary>
    /// The name of the connection string the directory's replicas use, <c>directory</c>.
    /// </summary>
    public const string ConnectionStringName = "directory";

    /// <summary>
    /// The name of the connection string the <c>migrate</c> command uses, <c>directory-migration</c>.
    /// </summary>
    public const string MigrationConnectionStringName = "directory-migration";

    /// <summary>
    /// The name of the configuration section that holds the runtime role the <c>migrate</c> command grants data access
    /// to, <c>DirectoryDatabase</c>.
    /// </summary>
    public const string SectionName = "DirectoryDatabase";

    /// <summary>
    /// The assembly that owns the EF Core migrations for the <see cref="DirectoryDbContext"/>. The directory server
    /// package ships no migrations.
    /// </summary>
    private static string MigrationsAssembly { get; } =
        typeof(DirectoryDatabase).Assembly.GetName().Name
        ?? throw new InvalidOperationException("The directory host assembly has no name.");

    /// <summary>
    /// Reads the connection string the host runs with: the <c>directory-migration</c> one for the <c>migrate</c>
    /// command, and the <c>directory</c> one otherwise.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="migrate">Whether the host runs the <c>migrate</c> command.</param>
    /// <returns>The connection string.</returns>
    /// <exception cref="InvalidOperationException">The connection string is not configured.</exception>
    public static string GetConnectionString(IConfiguration configuration, bool migrate)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var name = migrate ? MigrationConnectionStringName : ConnectionStringName;
        var connectionString = configuration.GetConnectionString(name);
        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException($"The connection string '{name}' is required.")
            : connectionString;
    }

    /// <summary>
    /// Reads the runtime role the <c>migrate</c> command grants data access to.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <returns>The trimmed role name.</returns>
    /// <exception cref="InvalidOperationException">
    /// The section holds a key that is not one of the <see cref="DirectoryDatabaseSettings"/> properties, or the role
    /// is missing, empty or not a valid role name. The message names the key and never holds the value.
    /// </exception>
    public static string GetRuntimeRole(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = new DirectoryDatabaseSettings();
        configuration.GetSection(SectionName).BindStrictly(settings);

        var key = $"{SectionName}:{nameof(DirectoryDatabaseSettings.RuntimeRole)}";

        var role = settings.RuntimeRole?.Trim();
        if (string.IsNullOrEmpty(role))
            throw new InvalidOperationException(
                $"The runtime role is not configured; set '{key}' to the name of the database role the "
                    + "directory's replicas connect as."
            );

        return DirectoryDatabaseGrants.IsValidRole(role)
            ? role
            : throw new InvalidOperationException(
                $"The '{key}' value is not a valid role name; it must match ^[a-z_][a-z0-9_]*$."
            );
    }

    /// <summary>
    /// Applies the directory's migrations, then grants <paramref name="runtimeRole"/> the data access it needs on every
    /// table and sequence, in one transaction, so a failed grant leaves no half-granted schema.
    /// </summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="runtimeRole">The runtime role to grant data access to.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the database is migrated and granted.</returns>
    public static async ValueTask MigrateAsync(
        IServiceProvider services,
        string runtimeRole,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        var dbContextFactory = services.GetRequiredService<IDbContextFactory<DirectoryDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);

        var statements = DirectoryDatabaseGrants.CreateStatements(
            dbContext.GetService<IDesignTimeModel>().Model,
            runtimeRole
        );

        await dbContext
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async token =>
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(
                        token
                    );
                    foreach (var statement in statements)
                        await dbContext.Database.ExecuteSqlRawAsync(statement, token);
                    await transaction.CommitAsync(token);
                },
                cancellationToken
            );
    }

    extension(DbContextOptionsBuilder builder)
    {
        /// <summary>
        /// Configures the options to use Npgsql against <paramref name="connectionString"/>, pointing migrations at this
        /// (the directory host) assembly.
        /// </summary>
        /// <param name="connectionString">The Postgres connection string.</param>
        /// <returns>The same <see cref="DbContextOptionsBuilder"/> instance for method chaining.</returns>
        public DbContextOptionsBuilder UseDirectoryNpgsql(string connectionString)
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder
                .UseNpgsql(
                    connectionString,
                    npgsql =>
                    {
                        npgsql.MigrationsAssembly(MigrationsAssembly);
                        npgsql.EnableRetryOnFailure();
                    }
                )
                .ReplaceService<IModelCustomizer, DirectoryNpgsqlModelCustomizer>();
        }
    }

    extension(IServiceCollection serviceCollection)
    {
        /// <summary>
        /// Registers the <see cref="DirectoryDbContext"/> factory against <paramref name="connectionString"/> and the
        /// <c>directory</c> readiness check, which holds the replica out of rotation while it cannot reach the database
        /// it serves every request from.
        /// </summary>
        /// <param name="connectionString">The Postgres connection string.</param>
        /// <returns>The same <see cref="IServiceCollection"/> instance for method chaining.</returns>
        public IServiceCollection AddDirectoryDatabase(string connectionString)
        {
            ArgumentNullException.ThrowIfNull(serviceCollection);

            serviceCollection.AddDbContextFactory<DirectoryDbContext>(dbOptions =>
                dbOptions.UseDirectoryNpgsql(connectionString)
            );

            serviceCollection
                .AddHealthChecks()
                .AddDbContextCheck<DirectoryDbContext>(
                    "directory",
                    tags: [HealthCheckTags.Readiness]
                );

            return serviceCollection;
        }
    }
}
