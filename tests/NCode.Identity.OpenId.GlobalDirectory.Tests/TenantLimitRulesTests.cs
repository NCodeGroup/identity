// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Settings;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class TenantLimitRulesTests : BaseTests
{
    #region MaxTenantsSettingName Tests

    [Fact]
    public void MaxTenantsSettingName_Always_EqualsTheOpenIdSettingName()
    {
        Assert.Equal(OpenIdSettingNames.MaxTenants, TenantLimitRules.MaxTenantsSettingName);
    }

    #endregion

    #region TryReadMaxTenants Tests

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"other":1}""")]
    public void TryReadMaxTenants_WhenTheSettingIsAbsent_ReturnsTrueWithNoLimit(string json)
    {
        var settings = JsonElement.Parse(json);

        var result = TenantLimitRules.TryReadMaxTenants(settings, out var maxTenants);

        Assert.True(result);
        Assert.Null(maxTenants);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(int.MaxValue)]
    public void TryReadMaxTenants_WhenTheSettingIsANonNegativeInteger_ReturnsTheLimit(int limit)
    {
        var settings = JsonElement.Parse($$"""{"max_tenants":{{limit}}}""");

        var result = TenantLimitRules.TryReadMaxTenants(settings, out var maxTenants);

        Assert.True(result);
        Assert.Equal(limit, maxTenants);
    }

    [Theory]
    [InlineData("""{"max_tenants":-1}""")]
    [InlineData("""{"max_tenants":1.5}""")]
    [InlineData("""{"max_tenants":"5"}""")]
    [InlineData("""{"max_tenants":null}""")]
    [InlineData("""{"max_tenants":2147483648}""")]
    public void TryReadMaxTenants_WhenTheSettingCannotBeRead_ReturnsFalse(string json)
    {
        var settings = JsonElement.Parse(json);

        var result = TenantLimitRules.TryReadMaxTenants(settings, out var maxTenants);

        Assert.False(result);
        Assert.Null(maxTenants);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"settings\"")]
    [InlineData("5")]
    public void TryReadMaxTenants_WhenTheDocumentIsNotAnObject_ReturnsFalse(string json)
    {
        var settings = JsonElement.Parse(json);

        var result = TenantLimitRules.TryReadMaxTenants(settings, out var maxTenants);

        Assert.False(result);
        Assert.Null(maxTenants);
    }

    [Fact]
    public void TryReadMaxTenants_WhenTheDocumentIsUndefined_ReturnsFalse()
    {
        var settings = default(JsonElement);

        var result = TenantLimitRules.TryReadMaxTenants(settings, out var maxTenants);

        Assert.False(result);
        Assert.Null(maxTenants);
    }

    #endregion

    #region GetEffectiveLimit Tests

    [Theory]
    [InlineData(5, 3, 3)]
    [InlineData(2, 7, 2)]
    [InlineData(0, 4, 0)]
    public void GetEffectiveLimit_WhenBothAreSet_ReturnsTheLesser(
        int? platformLimit,
        int? teamLimit,
        int? expected
    )
    {
        var effectiveLimit = TenantLimitRules.GetEffectiveLimit(platformLimit, teamLimit);

        Assert.Equal(expected, effectiveLimit);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(5, null, 5)]
    [InlineData(null, 3, 3)]
    public void GetEffectiveLimit_WhenOneIsUnset_ReturnsTheOther(
        int? platformLimit,
        int? teamLimit,
        int? expected
    )
    {
        var effectiveLimit = TenantLimitRules.GetEffectiveLimit(platformLimit, teamLimit);

        Assert.Equal(expected, effectiveLimit);
    }

    #endregion
}
