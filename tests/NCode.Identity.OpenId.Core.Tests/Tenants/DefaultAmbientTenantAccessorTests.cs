// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Tenants;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tenants;

public sealed class DefaultAmbientTenantAccessorTests
{
    private const string TenantId = "acme";
    private const string OtherTenantId = "contoso";
    private const string Reason = "background sweep";

    #region Default Tests

    [Fact]
    public void Default_NoScope_IsUnset()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        Assert.False(accessor.IsScoped);
        Assert.Null(accessor.TenantId);
        Assert.False(accessor.IsUnscoped);
        Assert.Null(accessor.UnscopedReason);
    }

    #endregion

    #region BeginScope Tests

    [Fact]
    public void BeginScope_TenantScope_ReportsTenant()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        using (accessor.BeginScope(TenantId))
        {
            Assert.True(accessor.IsScoped);
            Assert.Equal(TenantId, accessor.TenantId);
            Assert.False(accessor.IsUnscoped);
            Assert.Null(accessor.UnscopedReason);
        }
    }

    [Fact]
    public void BeginScope_NullTenantId_Throws()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        Assert.Throws<ArgumentNullException>(() => accessor.BeginScope(null!));
    }

    [Fact]
    public void BeginScope_Disposed_RestoresUnset()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        accessor.BeginScope(TenantId).Dispose();

        Assert.False(accessor.IsScoped);
        Assert.Null(accessor.TenantId);
        Assert.False(accessor.IsUnscoped);
    }

    [Fact]
    public void BeginScope_NestedTenantScope_RestoresOuterTenantOnDispose()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        using (accessor.BeginScope(TenantId))
        {
            using (accessor.BeginScope(OtherTenantId))
            {
                Assert.Equal(OtherTenantId, accessor.TenantId);
            }

            Assert.Equal(TenantId, accessor.TenantId);
        }

        Assert.Null(accessor.TenantId);
    }

    [Fact]
    public void BeginScope_DisposedTwice_RestoresOnlyOnce()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        using var outer = accessor.BeginScope(TenantId);
        var inner = accessor.BeginScope(OtherTenantId);
        inner.Dispose();
        using var unscoped = accessor.BeginUnscoped(Reason);
        inner.Dispose();

        Assert.True(accessor.IsUnscoped);
        Assert.Null(accessor.TenantId);
    }

    #endregion

    #region BeginUnscoped Tests

    [Fact]
    public void BeginUnscoped_Reason_ReportsUnscoped()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        using (accessor.BeginUnscoped(Reason))
        {
            Assert.True(accessor.IsUnscoped);
            Assert.Equal(Reason, accessor.UnscopedReason);
            Assert.False(accessor.IsScoped);
            Assert.Null(accessor.TenantId);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BeginUnscoped_BlankReason_Throws(string? reason)
    {
        var accessor = new DefaultAmbientTenantAccessor();

        Assert.ThrowsAny<ArgumentException>(() => accessor.BeginUnscoped(reason!));

        Assert.False(accessor.IsUnscoped);
    }

    [Fact]
    public void BeginUnscoped_Disposed_RestoresUnset()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        accessor.BeginUnscoped(Reason).Dispose();

        Assert.False(accessor.IsUnscoped);
        Assert.Null(accessor.UnscopedReason);
        Assert.False(accessor.IsScoped);
    }

    [Fact]
    public void BeginUnscoped_NestedTenantScope_YieldsTenantState()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        using (accessor.BeginUnscoped(Reason))
        {
            using (accessor.BeginScope(TenantId))
            {
                Assert.True(accessor.IsScoped);
                Assert.Equal(TenantId, accessor.TenantId);
                Assert.False(accessor.IsUnscoped);
                Assert.Null(accessor.UnscopedReason);
            }

            Assert.False(accessor.IsScoped);
            Assert.Null(accessor.TenantId);
            Assert.True(accessor.IsUnscoped);
            Assert.Equal(Reason, accessor.UnscopedReason);
        }

        Assert.False(accessor.IsUnscoped);
    }

    [Fact]
    public void BeginUnscoped_NestedInTenantScope_RestoresTenantOnDispose()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        using (accessor.BeginScope(TenantId))
        {
            using (accessor.BeginUnscoped(Reason))
            {
                Assert.True(accessor.IsUnscoped);
                Assert.Null(accessor.TenantId);
            }

            Assert.False(accessor.IsUnscoped);
            Assert.Equal(TenantId, accessor.TenantId);
        }
    }

    [Fact]
    public async Task BeginUnscoped_AcrossAwait_FlowsToContinuation()
    {
        var accessor = new DefaultAmbientTenantAccessor();

        using (accessor.BeginUnscoped(Reason))
        {
            await Task.Yield();

            Assert.True(accessor.IsUnscoped);
            Assert.Equal(Reason, accessor.UnscopedReason);
        }
    }

    #endregion
}
