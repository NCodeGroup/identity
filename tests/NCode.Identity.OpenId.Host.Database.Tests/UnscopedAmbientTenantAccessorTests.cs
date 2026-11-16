// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.Host.Database.Tests;

public sealed class UnscopedAmbientTenantAccessorTests
{
    #region IsScoped Property Tests

    [Fact]
    public void IsScoped_Always_IsFalse()
    {
        var accessor = new UnscopedAmbientTenantAccessor();

        Assert.False(accessor.IsScoped);
    }

    #endregion

    #region TenantId Property Tests

    [Fact]
    public void TenantId_Always_IsNull()
    {
        var accessor = new UnscopedAmbientTenantAccessor();

        Assert.Null(accessor.TenantId);
    }

    #endregion

    #region IsUnscoped Property Tests

    [Fact]
    public void IsUnscoped_Always_IsTrue()
    {
        var accessor = new UnscopedAmbientTenantAccessor();

        Assert.True(accessor.IsUnscoped);
    }

    #endregion

    #region UnscopedReason Property Tests

    [Fact]
    public void UnscopedReason_Always_IsSchemaMigration()
    {
        var accessor = new UnscopedAmbientTenantAccessor();

        Assert.Equal("schema migration", accessor.UnscopedReason);
    }

    #endregion

    #region BeginScope Tests

    [Fact]
    public void BeginScope_WhenCalled_ThrowsNotSupported()
    {
        var accessor = new UnscopedAmbientTenantAccessor();

        var exception = Assert.Throws<NotSupportedException>(() => accessor.BeginScope("tenant-a"));

        Assert.Equal("Unscoped construction cannot begin a tenant scope.", exception.Message);
    }

    #endregion

    #region BeginUnscoped Tests

    [Fact]
    public void BeginUnscoped_WhenCalled_ThrowsNotSupported()
    {
        var accessor = new UnscopedAmbientTenantAccessor();

        var exception = Assert.Throws<NotSupportedException>(() =>
            accessor.BeginUnscoped("another reason")
        );

        Assert.Equal("Unscoped construction cannot begin another scope.", exception.Message);
    }

    #endregion
}
