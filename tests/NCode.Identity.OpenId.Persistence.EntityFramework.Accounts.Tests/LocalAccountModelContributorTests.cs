// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Accounts;

public sealed class LocalAccountModelContributorTests : IDisposable
{
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }

    public LocalAccountModelContributorTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
    }

    #region Model Tests

    [Theory]
    [InlineData(typeof(LocalAccountEntity))]
    [InlineData(typeof(LocalAccountClaimEntity))]
    public void Model_LocalAccount_HasRestrictForeignKeyToTenant(Type entityClrType)
    {
        var entityType = DbContext.Model.FindEntityType(entityClrType);
        Assert.NotNull(entityType);

        var foreignKey = Assert.Single(
            entityType.GetForeignKeys(),
            candidate => candidate.PrincipalEntityType.ClrType == typeof(TenantEntity)
        );

        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.True(foreignKey.IsRequired);
    }

    #endregion
}
