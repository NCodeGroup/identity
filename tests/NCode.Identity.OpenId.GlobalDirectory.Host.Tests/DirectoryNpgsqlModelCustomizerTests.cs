// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using NCode.Identity.OpenId.GlobalDirectory.Server;
using NCode.Identity.OpenId.GlobalDirectory.Server.Entities;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Host.Tests;

public sealed class DirectoryNpgsqlModelCustomizerTests
{
    private static DirectoryDbContext CreateContext() =>
        new DirectoryDbContextDesignTimeFactory().CreateDbContext([]);

    private static DirectoryDbContext CreateContextWithoutTheCustomizer()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DirectoryDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=directory");
        return new DirectoryDbContext(optionsBuilder.Options);
    }

    private static string? GetNormalizedPathCollation(DirectoryDbContext context) =>
        context
            .GetService<IDesignTimeModel>()
            .Model.FindEntityType(typeof(TeamRecord))
            ?.FindProperty(nameof(TeamRecord.NormalizedPath))
            ?.GetCollation();

    #region PathCollation Tests

    [Fact]
    public void PathCollation_WhenRead_IsTheByteOrderCollation()
    {
        Assert.Equal("C", DirectoryNpgsqlModelCustomizer.PathCollation);
    }

    #endregion

    #region CreateDbContext Tests

    [Fact]
    public void CreateDbContext_WhenCalled_UsesNpgsql()
    {
        using var context = CreateContext();

        Assert.True(context.Database.IsNpgsql());
    }

    [Fact]
    public void CreateDbContext_WhenCalled_PointsMigrationsAtTheHostAssembly()
    {
        using var context = CreateContext();

        var migrationsAssembly = context.GetService<IMigrationsAssembly>();

        Assert.Same(typeof(DirectoryDatabase).Assembly, migrationsAssembly.Assembly);
    }

    #endregion

    #region Customize Tests

    [Fact]
    public void Customize_WhenTheModelIsBuilt_OrdersTeamPathsByTheirBytes()
    {
        using var control = CreateContextWithoutTheCustomizer();
        Assert.Null(GetNormalizedPathCollation(control));

        using var context = CreateContext();

        var collation = GetNormalizedPathCollation(context);

        Assert.Equal(DirectoryNpgsqlModelCustomizer.PathCollation, collation);
    }

    #endregion
}
