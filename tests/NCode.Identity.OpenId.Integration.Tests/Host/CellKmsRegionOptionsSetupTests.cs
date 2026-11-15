// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Host;
using NCode.Identity.Secrets.AwsKms.Options;
using NCode.Identity.Secrets.GoogleCloudKms.Options;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Host;

/// <summary>
/// Provides unit tests for the <see cref="CellKmsRegionOptionsSetup"/> class.
/// </summary>
public class CellKmsRegionOptionsSetupTests
{
    private static CellKmsRegionOptionsSetup CreateSetup(params string[] cloudRegions) =>
        new(Options.Create(new CellOptions { CloudRegions = cloudRegions }));

    #region AwsKmsKeyWrapperOptions Tests

    [Fact]
    public void PostConfigure_CloudRegionsSet_SetsRegion_AwsKmsKeyWrapperOptions()
    {
        var options = new AwsKmsKeyWrapperOptions { AllowedRegions = ["eu-west-1"] };

        CreateSetup("us-east-1", "us-west-2").PostConfigure(name: null, options);

        Assert.Equal(["us-east-1", "us-west-2"], options.AllowedRegions);
    }

    [Fact]
    public void PostConfigure_NoCloudRegion_LeavesOptionsUnchanged_AwsKmsKeyWrapperOptions()
    {
        var options = new AwsKmsKeyWrapperOptions { AllowedRegions = ["eu-west-1"] };

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal(["eu-west-1"], options.AllowedRegions);
    }

    #endregion

    #region AwsKmsRemoteKeyProviderOptions Tests

    [Fact]
    public void PostConfigure_CloudRegionsSet_SetsRegion_AwsKmsRemoteKeyProviderOptions()
    {
        var options = new AwsKmsRemoteKeyProviderOptions { AllowedRegions = ["eu-west-1"] };

        CreateSetup("us-east-1", "us-west-2").PostConfigure(name: null, options);

        Assert.Equal(["us-east-1", "us-west-2"], options.AllowedRegions);
    }

    [Fact]
    public void PostConfigure_NoCloudRegion_LeavesOptionsUnchanged_AwsKmsRemoteKeyProviderOptions()
    {
        var options = new AwsKmsRemoteKeyProviderOptions { AllowedRegions = ["eu-west-1"] };

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal(["eu-west-1"], options.AllowedRegions);
    }

    #endregion

    #region GoogleCloudKmsKeyWrapperOptions Tests

    [Fact]
    public void PostConfigure_CloudRegionsSet_SetsRegion_GoogleCloudKmsKeyWrapperOptions()
    {
        var options = new GoogleCloudKmsKeyWrapperOptions { AllowedLocations = ["europe-west1"] };

        CreateSetup("us-east1", "us-west1").PostConfigure(name: null, options);

        Assert.Equal(["us-east1", "us-west1"], options.AllowedLocations);
    }

    [Fact]
    public void PostConfigure_NoCloudRegion_LeavesOptionsUnchanged_GoogleCloudKmsKeyWrapperOptions()
    {
        var options = new GoogleCloudKmsKeyWrapperOptions { AllowedLocations = ["europe-west1"] };

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal(["europe-west1"], options.AllowedLocations);
    }

    #endregion

    #region GoogleCloudKmsRemoteKeyProviderOptions Tests

    [Fact]
    public void PostConfigure_CloudRegionsSet_SetsRegion_GoogleCloudKmsRemoteKeyProviderOptions()
    {
        var options = new GoogleCloudKmsRemoteKeyProviderOptions
        {
            AllowedLocations = ["europe-west1"],
        };

        CreateSetup("us-east1", "us-west1").PostConfigure(name: null, options);

        Assert.Equal(["us-east1", "us-west1"], options.AllowedLocations);
    }

    [Fact]
    public void PostConfigure_NoCloudRegion_LeavesOptionsUnchanged_GoogleCloudKmsRemoteKeyProviderOptions()
    {
        var options = new GoogleCloudKmsRemoteKeyProviderOptions
        {
            AllowedLocations = ["europe-west1"],
        };

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal(["europe-west1"], options.AllowedLocations);
    }

    #endregion
}
