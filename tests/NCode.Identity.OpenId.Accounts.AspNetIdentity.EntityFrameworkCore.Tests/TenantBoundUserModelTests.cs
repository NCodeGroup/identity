// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NCode.Identity.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity.EntityFrameworkCore;

public sealed class TenantBoundUserModelTests
{
    private const int IdentityDefaultLookupLength = 256;

    #region HasTenantLookupLengths Tests

    [Fact]
    public void HasTenantLookupLengths_WhenCalled_SizesTheLookupColumnsForTenantPrefixedValues()
    {
        using var dbContext = new TenantBoundIdentityDbContext(
            CreateOptions<TenantBoundIdentityDbContext>()
        );

        Assert.Equal(
            AccountMaxLengths.UserName,
            GetMaxLength(dbContext, nameof(TestTenantUser.UserName))
        );
        Assert.Equal(
            AccountMaxLengths.NormalizedUserName,
            GetMaxLength(dbContext, nameof(TestTenantUser.NormalizedUserName))
        );
        Assert.Equal(
            AccountMaxLengths.Email,
            GetMaxLength(dbContext, nameof(TestTenantUser.Email))
        );
        Assert.Equal(
            AccountMaxLengths.NormalizedEmail,
            GetMaxLength(dbContext, nameof(TestTenantUser.NormalizedEmail))
        );
    }

    [Fact]
    public void HasTenantLookupLengths_WhenCalled_SizesTheDisplayNameColumnAndRequiresIt()
    {
        var options = CreateOptions<TenantBoundIdentityDbContext>();
        using var dbContext = new TenantBoundIdentityDbContext(options);

        var maxLength = GetMaxLength(dbContext, nameof(TestTenantUser.DisplayName));
        var isNullable = IsNullable(dbContext, nameof(TestTenantUser.DisplayName));

        Assert.Equal(AccountMaxLengths.DisplayName, maxLength);
        Assert.False(isNullable);
    }

    [Fact]
    public void HasTenantLookupLengths_WhenCalled_SizesTheConnectionIdColumnAndRequiresIt()
    {
        var options = CreateOptions<TenantBoundIdentityDbContext>();
        using var dbContext = new TenantBoundIdentityDbContext(options);

        var maxLength = GetMaxLength(dbContext, nameof(TestTenantUser.ConnectionId));
        var isNullable = IsNullable(dbContext, nameof(TestTenantUser.ConnectionId));

        Assert.Equal(MaxLengths.ResourceId, maxLength);
        Assert.False(isNullable);
    }

    [Fact]
    public void HasTenantLookupLengths_WhenTheBuilderIsNull_Throws()
    {
        EntityTypeBuilder<TestTenantUser> builder = null!;

        var exception = Assert.Throws<ArgumentNullException>(() =>
            builder.HasTenantLookupLengths()
        );

        Assert.Equal("builder", exception.ParamName);
    }

    #endregion

    #region Identity Model Tests

    [Fact]
    public void IdentityModel_WithoutHasTenantLookupLengths_SizesTheLookupColumnsAt256()
    {
        using var dbContext = new IdentityDbContext<TestTenantUser>(
            CreateOptions<IdentityDbContext<TestTenantUser>>()
        );

        Assert.Equal(
            IdentityDefaultLookupLength,
            GetMaxLength(dbContext, nameof(TestTenantUser.UserName))
        );
        Assert.Equal(
            IdentityDefaultLookupLength,
            GetMaxLength(dbContext, nameof(TestTenantUser.NormalizedUserName))
        );
        Assert.Equal(
            IdentityDefaultLookupLength,
            GetMaxLength(dbContext, nameof(TestTenantUser.Email))
        );
        Assert.Equal(
            IdentityDefaultLookupLength,
            GetMaxLength(dbContext, nameof(TestTenantUser.NormalizedEmail))
        );
        Assert.True(AccountMaxLengths.NormalizedUserName > IdentityDefaultLookupLength);
        Assert.True(AccountMaxLengths.NormalizedEmail > IdentityDefaultLookupLength);
    }

    #endregion

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext
    {
        var databaseName = Guid.NewGuid().ToString();
        return new DbContextOptionsBuilder<TContext>().UseInMemoryDatabase(databaseName).Options;
    }

    private static int? GetMaxLength(DbContext dbContext, string propertyName)
    {
        var userType = dbContext.Model.FindEntityType(typeof(TestTenantUser));
        Assert.NotNull(userType);
        var property = userType.FindProperty(propertyName);
        Assert.NotNull(property);

        return property.GetMaxLength();
    }

    private static bool IsNullable(DbContext dbContext, string propertyName)
    {
        var userType = dbContext.Model.FindEntityType(typeof(TestTenantUser));
        Assert.NotNull(userType);
        var property = userType.FindProperty(propertyName);
        Assert.NotNull(property);

        return property.IsNullable;
    }

    private sealed class TenantBoundIdentityDbContext(DbContextOptions options)
        : IdentityDbContext<TestTenantUser>(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<TestTenantUser>().HasTenantLookupLengths();
        }
    }
}
