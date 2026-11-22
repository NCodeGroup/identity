// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Collections.Providers;
using NCode.Identity.OpenId.Authentication.Auditing;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Auditing;

public sealed class DefaultAuditStreamSettingDescriptorDataSourceTests : BaseTests
{
    private DefaultAuditStreamSettingDescriptorDataSource CreateDataSource() =>
        new(CreateStrictMock<INullChangeToken>().Object);

    #region Collection Property Tests

    [Fact]
    public void Collection_Always_DescribesALimitMergedAsACeiling()
    {
        var dataSource = CreateDataSource();

        var descriptor = Assert.IsType<SettingDescriptor<int>>(
            Assert.Single(dataSource.Collection)
        );

        Assert.Equal(OpenIdSettingNames.MaxAuditStreams, descriptor.Name);
        Assert.Equal<string>([OpenIdSettingScopes.Tenant], descriptor.AppliesTo);
        Assert.True(descriptor.IsLimit);
        Assert.False(descriptor.IsDiscoverable);
        Assert.False(descriptor.TryValidate(-1, out _));
        Assert.True(descriptor.TryValidate(0, out _));
        Assert.True(descriptor.TryValidate(10, out _));
        Assert.False(descriptor.TryValidate(11, out _));
        Assert.Equal(3, descriptor.OnMerge(5, 3));
        Assert.Equal(5, descriptor.OnMerge(5, 8));
    }

    [Fact]
    public void Collection_Always_DefaultsToTwo()
    {
        var dataSource = CreateDataSource();

        var descriptor = Assert.IsType<SettingDescriptor<int>>(
            Assert.Single(dataSource.Collection)
        );

        Assert.Equal(2, descriptor.Default);
    }

    #endregion
}
