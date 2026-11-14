// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Accounts.AspNetIdentity;

public sealed class TenantLookupNormalizerTests
{
    private TestAmbientTenantAccessor Accessor { get; } = new();
    private TenantLookupNormalizer Normalizer { get; }

    public TenantLookupNormalizerTests()
    {
        Normalizer = new TenantLookupNormalizer(Accessor);
    }

    #region NormalizeName Tests

    [Fact]
    public void NormalizeName_WhenTenantScoped_PrefixesTheNormalizedTenantId()
    {
        using var scope = Accessor.BeginScope("Tenant-A");

        var normalized = Normalizer.NormalizeName("Alice");

        Assert.Equal("tenant-a|ALICE", normalized);
    }

    [Fact]
    public void NormalizeName_WhenTenantScopedToDifferentTenants_ReturnsDifferentValues()
    {
        string? first;
        string? second;
        using (Accessor.BeginScope("tenant-a"))
        {
            first = Normalizer.NormalizeName("alice");
        }

        using (Accessor.BeginScope("tenant-b"))
        {
            second = Normalizer.NormalizeName("alice");
        }

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NormalizeName_WhenUnscoped_ReturnsTheUpperInvariantValue()
    {
        using var scope = Accessor.BeginUnscoped("test");

        var normalized = Normalizer.NormalizeName("Alice");

        Assert.Equal("ALICE", normalized);
    }

    [Fact]
    public void NormalizeName_WhenUnset_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Normalizer.NormalizeName("alice"));
    }

    [Fact]
    public void NormalizeName_WhenTenantScopedAndNameIsNull_ReturnsNull()
    {
        using var scope = Accessor.BeginScope("tenant-a");

        var normalized = Normalizer.NormalizeName(null);

        Assert.Null(normalized);
    }

    #endregion

    #region NormalizeEmail Tests

    [Fact]
    public void NormalizeEmail_WhenTenantScoped_PrefixesTheNormalizedTenantId()
    {
        using var scope = Accessor.BeginScope("Tenant-A");

        var normalized = Normalizer.NormalizeEmail("Alice@Example.com");

        Assert.Equal("tenant-a|ALICE@EXAMPLE.COM", normalized);
    }

    [Fact]
    public void NormalizeEmail_WhenUnscoped_ReturnsTheUpperInvariantValue()
    {
        using var scope = Accessor.BeginUnscoped("test");

        var normalized = Normalizer.NormalizeEmail("Alice@Example.com");

        Assert.Equal("ALICE@EXAMPLE.COM", normalized);
    }

    [Fact]
    public void NormalizeEmail_WhenUnset_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Normalizer.NormalizeEmail("alice@example.com")
        );
    }

    [Fact]
    public void NormalizeEmail_WhenTenantScopeIsNestedInUnscopedScope_PrefixesTheTenant()
    {
        using var unscoped = Accessor.BeginUnscoped("test");
        using var scope = Accessor.BeginScope("tenant-a");

        var normalized = Normalizer.NormalizeEmail("alice@example.com");

        Assert.Equal("tenant-a|ALICE@EXAMPLE.COM", normalized);
    }

    #endregion
}
