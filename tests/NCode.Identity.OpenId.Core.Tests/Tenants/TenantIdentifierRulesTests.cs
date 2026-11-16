// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Tenants;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tenants;

public sealed class TenantIdentifierRulesTests
{
    #region IsValidTenantId Tests

    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("acme-dev")]
    [InlineData("a1-b2-c3")]
    public void IsValidTenantId_WithALowercaseHostLabel_ReturnsTrue(string tenantId)
    {
        Assert.True(TenantIdentifierRules.IsValidTenantId(tenantId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Acme")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme_dev")]
    [InlineData("acme\n")]
    public void IsValidTenantId_WithAnInvalidIdentifier_ReturnsFalse(string? tenantId)
    {
        Assert.False(TenantIdentifierRules.IsValidTenantId(tenantId));
    }

    [Fact]
    public void IsValidTenantId_WithMaxTenantIdLengthCharacters_ReturnsTrue()
    {
        var tenantId = "a" + new string('b', TenantIdentifierRules.MaxTenantIdLength - 1);

        Assert.True(TenantIdentifierRules.IsValidTenantId(tenantId));
    }

    [Fact]
    public void IsValidTenantId_WithOneMoreThanMaxTenantIdLengthCharacters_ReturnsFalse()
    {
        var tenantId = "a" + new string('b', TenantIdentifierRules.MaxTenantIdLength);

        Assert.False(TenantIdentifierRules.IsValidTenantId(tenantId));
    }

    #endregion
}
