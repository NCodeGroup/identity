// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Tests;

public sealed class DirectoryDatabaseTests
{
    private const string ConnectionString =
        "Host=string-host;Database=string-db;Username=string-user;Password=string-secret";
    private const string Host = "db.example.test";
    private const string Port = "6543";
    private const string Name = "directory";
    private const string Username = "directory-user";
    private const string Password = "pa55-w0rd";
    private const string RuntimeRoleKey = "DirectoryMigration:RuntimeRole";
    private const string InitialCreate = "20261125123607_InitialCreate";
    private const string UnknownInitialCreate = "20261001000000_InitialCreate";

    private static IConfiguration CreateConfiguration(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))
            )
            .Build();

    private static (string Key, string? Value)[] CreateParts() =>
        [
            ("DirectoryDatabase:Host", Host),
            ("DirectoryDatabase:Port", Port),
            ("DirectoryDatabase:Name", Name),
            ("DirectoryDatabase:Username", Username),
            ("DirectoryDatabase:Password", Password),
        ];

    #region GetConnectionString Tests

    [Fact]
    public void GetConnectionString_WhenTheConfigurationIsNull_Throws()
    {
        IConfiguration configuration = null!;

        var exception = Assert.Throws<ArgumentNullException>(() =>
            DirectoryDatabase.GetConnectionString(configuration)
        );

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void GetConnectionString_WhenTheConnectionStringIsConfigured_ReturnsIt()
    {
        var configuration = CreateConfiguration(("ConnectionStrings:directory", ConnectionString));

        var result = DirectoryDatabase.GetConnectionString(configuration);

        Assert.Equal(ConnectionString, result);
    }

    [Fact]
    public void GetConnectionString_WhenThePartsAreConfigured_AssemblesThem()
    {
        var configuration = CreateConfiguration(CreateParts());

        var result = DirectoryDatabase.GetConnectionString(configuration);

        var builder = new DbConnectionStringBuilder { ConnectionString = result };
        Assert.Equal(Host, builder["Host"]);
        Assert.Equal(Port, builder["Port"]);
        Assert.Equal(Name, builder["Database"]);
        Assert.Equal(Username, builder["Username"]);
        Assert.Equal(Password, builder["Password"]);
    }

    [Fact]
    public void GetConnectionString_WhenBothFormsAreConfigured_ThrowsNamingBothForms()
    {
        var configuration = CreateConfiguration([
            ("ConnectionStrings:directory", ConnectionString),
            .. CreateParts(),
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DirectoryDatabase.GetConnectionString(configuration)
        );

        Assert.Contains("ConnectionStrings:directory", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DirectoryDatabase", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ConnectionString, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConnectionString_WhenNeitherFormIsConfigured_ThrowsNamingBothForms()
    {
        var configuration = CreateConfiguration();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DirectoryDatabase.GetConnectionString(configuration)
        );

        Assert.Equal(
            "The directory's database is not configured; set the connection string "
                + "'ConnectionStrings:directory' or the 'DirectoryDatabase' parts.",
            exception.Message
        );
    }

    #endregion

    #region GetRuntimeRole Tests

    [Fact]
    public void GetRuntimeRole_WhenTheConfigurationIsNull_Throws()
    {
        IConfiguration configuration = null!;

        var exception = Assert.Throws<ArgumentNullException>(() =>
            DirectoryDatabase.GetRuntimeRole(configuration)
        );

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void GetRuntimeRole_WhenTheRoleIsConfigured_ReturnsTheTrimmedRole()
    {
        var configuration = CreateConfiguration((RuntimeRoleKey, "  directory_runtime\t"));

        var result = DirectoryDatabase.GetRuntimeRole(configuration);

        Assert.Equal("directory_runtime", result);
    }

    [Fact]
    public void GetRuntimeRole_WhenTheRoleIsMissing_ThrowsNamingTheKey()
    {
        var configuration = CreateConfiguration();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DirectoryDatabase.GetRuntimeRole(configuration)
        );

        Assert.Contains(RuntimeRoleKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRuntimeRole_WhenTheSectionHoldsAnUnknownKey_Throws()
    {
        var configuration = CreateConfiguration(
            (RuntimeRoleKey, "directory_runtime"),
            ("DirectoryMigration:RuntimRole", "other")
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DirectoryDatabase.GetRuntimeRole(configuration)
        );

        Assert.Contains("DirectoryMigration", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RuntimRole", exception.Message, StringComparison.Ordinal);
    }

    #endregion

    #region ThrowIfUnknownMigrationsApplied Tests

    [Fact]
    public void ThrowIfUnknownMigrationsApplied_WhenNoMigrationIsApplied_DoesNotThrow()
    {
        DirectoryDatabase.ThrowIfUnknownMigrationsApplied([], [InitialCreate]);
    }

    [Fact]
    public void ThrowIfUnknownMigrationsApplied_WhenEveryAppliedMigrationIsKnown_DoesNotThrow()
    {
        DirectoryDatabase.ThrowIfUnknownMigrationsApplied([InitialCreate], [InitialCreate]);
    }

    [Fact]
    public void ThrowIfUnknownMigrationsApplied_WhenTheHistoryHoldsAnUnknownMigration_ThrowsNamingIt()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DirectoryDatabase.ThrowIfUnknownMigrationsApplied(
                [InitialCreate, UnknownInitialCreate],
                [InitialCreate]
            )
        );

        Assert.Equal(
            $"The directory database's DirectoryDbContext migrations history records {UnknownInitialCreate}, which "
                + "this host does not hold, so its migrations cannot be applied to it. The directory's DbContext has "
                + "one InitialCreate, which is re-scaffolded with a fresh identifier on each model change. Drop and "
                + "recreate the directory's database, then run the migrate command again.",
            exception.Message
        );
    }

    #endregion
}
