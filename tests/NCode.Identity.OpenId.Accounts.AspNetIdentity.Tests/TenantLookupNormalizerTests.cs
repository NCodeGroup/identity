// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence;

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
        using var connection = AmbientLookupConnection.BeginScope("Connection-1");

        var normalized = Normalizer.NormalizeName("Alice");

        Assert.Equal("tenant-a|connection-1|ALICE", normalized);
    }

    [Fact]
    public void NormalizeName_WhenTenantScopedToDifferentTenants_ReturnsDifferentValues()
    {
        string? first;
        string? second;
        using var connection = AmbientLookupConnection.BeginScope("connection-1");
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
    public void NormalizeName_WhenScopedToDifferentConnections_ReturnsDifferentValues()
    {
        using var scope = Accessor.BeginScope("tenant-a");
        string? first;
        string? second;
        using (AmbientLookupConnection.BeginScope("connection-1"))
        {
            first = Normalizer.NormalizeName("alice");
        }

        using (AmbientLookupConnection.BeginScope("connection-2"))
        {
            second = Normalizer.NormalizeName("alice");
        }

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NormalizeName_WithoutConnectionScope_Throws()
    {
        using var scope = Accessor.BeginScope("tenant-a");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Normalizer.NormalizeName("alice")
        );

        Assert.Contains("connection scope", exception.Message);
    }

    [Fact]
    public void NormalizeName_WhenAConnectionScopeIsDisposed_RestoresTheEnclosingConnection()
    {
        using var scope = Accessor.BeginScope("tenant-a");
        using var outer = AmbientLookupConnection.BeginScope("connection-1");
        using (AmbientLookupConnection.BeginScope("connection-2")) { }

        var normalized = Normalizer.NormalizeName("alice");

        Assert.Equal("tenant-a|connection-1|ALICE", normalized);
    }

    [Fact]
    public void NormalizeName_WhenUnscoped_Throws()
    {
        using var scope = Accessor.BeginUnscoped("test");

        Assert.Throws<InvalidOperationException>(() => Normalizer.NormalizeName("Alice"));
    }

    [Fact]
    public void NormalizeName_WhenUnset_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Normalizer.NormalizeName("alice"));
    }

    [Fact]
    public void NormalizeName_WithoutTenantScope_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Normalizer.NormalizeName("alice")
        );

        Assert.Contains("tenant scope", exception.Message);
    }

    [Fact]
    public void NormalizeName_WhenTenantScopedAndNameIsNull_ReturnsNull()
    {
        using var scope = Accessor.BeginScope("tenant-a");

        var normalized = Normalizer.NormalizeName(null);

        Assert.Null(normalized);
    }

    [Fact]
    public void NormalizeName_WhenTheTenantAndTheNameAreTheLongest_FitsNormalizedUserName()
    {
        var tenant = new string('t', 63);
        Assert.True(TenantIdentifierRules.IsValidTenantId(tenant));
        Assert.False(TenantIdentifierRules.IsValidTenantId(tenant + "t"));
        using var scope = Accessor.BeginScope(tenant);
        using var connection = AmbientLookupConnection.BeginScope(
            new string('c', MaxLengths.ResourceId)
        );

        var normalized = Normalizer.NormalizeName(new string('a', AccountMaxLengths.UserName));

        Assert.NotNull(normalized);
        Assert.Equal(AccountMaxLengths.NormalizedUserName, normalized.Length);
    }

    #endregion

    #region NormalizeEmail Tests

    [Fact]
    public void NormalizeEmail_WhenTenantScoped_PrefixesTheNormalizedTenantId()
    {
        using var scope = Accessor.BeginScope("Tenant-A");
        using var connection = AmbientLookupConnection.BeginScope("Connection-1");

        var normalized = Normalizer.NormalizeEmail("Alice@Example.com");

        Assert.Equal("tenant-a|connection-1|ALICE@EXAMPLE.COM", normalized);
    }

    [Fact]
    public void NormalizeEmail_WhenUnscoped_Throws()
    {
        using var scope = Accessor.BeginUnscoped("test");

        Assert.Throws<InvalidOperationException>(() =>
            Normalizer.NormalizeEmail("Alice@Example.com")
        );
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
        using var connection = AmbientLookupConnection.BeginScope("connection-1");

        var normalized = Normalizer.NormalizeEmail("alice@example.com");

        Assert.Equal("tenant-a|connection-1|ALICE@EXAMPLE.COM", normalized);
    }

    [Fact]
    public void NormalizeEmail_WhenTheTenantAndTheEmailAreTheLongest_FitsNormalizedEmail()
    {
        var tenant = new string('t', 63);
        Assert.True(TenantIdentifierRules.IsValidTenantId(tenant));
        Assert.False(TenantIdentifierRules.IsValidTenantId(tenant + "t"));
        using var scope = Accessor.BeginScope(tenant);
        using var connection = AmbientLookupConnection.BeginScope(
            new string('c', MaxLengths.ResourceId)
        );

        var normalized = Normalizer.NormalizeEmail(new string('a', AccountMaxLengths.Email));

        Assert.NotNull(normalized);
        Assert.Equal(AccountMaxLengths.NormalizedEmail, normalized.Length);
    }

    #endregion
}
