// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NCode.Identity.OpenId.Host.Database.Tests;

public sealed class HostDatabaseTests
{
    private const string ConnectionString = "Host=localhost;Database=openiddb";

    #region UseOpenIdNpgsql Tests

    [Fact]
    public void UseOpenIdNpgsql_WithAccessor_AddsTheTenantSessionInterceptor()
    {
        var builder = new DbContextOptionsBuilder();

        builder.UseOpenIdNpgsql(ConnectionString, new UnscopedAmbientTenantAccessor());

        var interceptors = builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors;
        Assert.NotNull(interceptors);
        Assert.Single(interceptors.OfType<TenantSessionInterceptor>());
    }

    #endregion

    #region CreateUnscopedOpenIdContext Tests

    [Fact]
    public async Task CreateUnscopedOpenIdContext_WhenCalled_ReadsAcrossEveryTenant()
    {
        await using var serviceProvider = OpenIdDbContextDesignTimeFactory.CreateServiceProvider();

        await using var context = HostDatabase.CreateUnscopedOpenIdContext(
            serviceProvider,
            ConnectionString
        );

        Assert.True(context.IsUnscoped);
        Assert.Null(context.NormalizedAmbientTenantId);
    }

    [Fact]
    public async Task CreateUnscopedOpenIdContext_WhenCalled_CarriesTheScopeIntoTheSession()
    {
        await using var serviceProvider = OpenIdDbContextDesignTimeFactory.CreateServiceProvider();

        await using var context = HostDatabase.CreateUnscopedOpenIdContext(
            serviceProvider,
            ConnectionString
        );

        var options = context.GetService<IDbContextOptions>();
        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors;
        Assert.NotNull(interceptors);
        Assert.Single(interceptors.OfType<TenantSessionInterceptor>());
    }

    [Fact]
    public async Task CreateUnscopedOpenIdContext_WhenCalled_UsesNpgsqlWithTheHostMigrations()
    {
        await using var serviceProvider = OpenIdDbContextDesignTimeFactory.CreateServiceProvider();

        await using var context = HostDatabase.CreateUnscopedOpenIdContext(
            serviceProvider,
            ConnectionString
        );

        var relational = RelationalOptionsExtension.Extract(
            context.GetService<IDbContextOptions>()
        );
        Assert.True(context.Database.IsNpgsql());
        Assert.Equal(ConnectionString, relational.ConnectionString);
        Assert.Equal(HostDatabase.MigrationsAssembly, relational.MigrationsAssembly);
    }

    #endregion

    #region CreateDataProtectionContext Tests

    [Fact]
    public void CreateDataProtectionContext_WhenCalled_KeepsItsOwnMigrationsHistory()
    {
        using var context = HostDatabase.CreateDataProtectionContext(ConnectionString);

        var relational = RelationalOptionsExtension.Extract(
            context.GetService<IDbContextOptions>()
        );
        Assert.True(context.Database.IsNpgsql());
        Assert.Equal(ConnectionString, relational.ConnectionString);
        Assert.Equal(HostDatabase.MigrationsAssembly, relational.MigrationsAssembly);
        Assert.Equal("__EFMigrationsHistory_DataProtection", relational.MigrationsHistoryTableName);
    }

    #endregion
}
