// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Secrets.GoogleCloudKms.Options;
using NCode.Identity.Secrets.Options;
using Xunit;

namespace NCode.Identity.Secrets.GoogleCloudKms.Tests.Options;

public class GoogleCloudKmsKeyResidencyOptionsSetupTests
{
    private static GoogleCloudKmsKeyResidencyOptionsSetup CreateSetup(
        params string[] cloudRegions
    ) =>
        new(
            Microsoft.Extensions.Options.Options.Create(
                new KeyResidencyOptions { CloudRegions = [.. cloudRegions] }
            )
        );

    #region PostConfigure Tests

    [Fact]
    public void PostConfigure_KeyWrapperOptionsWithCloudRegions_ReplacesTheAllowedLocations()
    {
        var options = new GoogleCloudKmsKeyWrapperOptions { AllowedLocations = ["europe-west1"] };

        CreateSetup("us-central1").PostConfigure(name: null, options);

        Assert.Equal(["us-central1"], options.AllowedLocations);
    }

    [Fact]
    public void PostConfigure_KeyWrapperOptionsWithoutCloudRegions_LeavesTheAllowedLocations()
    {
        var options = new GoogleCloudKmsKeyWrapperOptions { AllowedLocations = ["europe-west1"] };

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal(["europe-west1"], options.AllowedLocations);
    }

    [Fact]
    public void PostConfigure_RemoteKeyProviderOptionsWithCloudRegions_ReplacesTheAllowedLocations()
    {
        var options = new GoogleCloudKmsRemoteKeyProviderOptions
        {
            AllowedLocations = ["europe-west1"],
        };

        CreateSetup("us-central1").PostConfigure(name: null, options);

        Assert.Equal(["us-central1"], options.AllowedLocations);
    }

    [Fact]
    public void PostConfigure_RemoteKeyProviderOptionsWithoutCloudRegions_LeavesTheAllowedLocations()
    {
        var options = new GoogleCloudKmsRemoteKeyProviderOptions
        {
            AllowedLocations = ["europe-west1"],
        };

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal(["europe-west1"], options.AllowedLocations);
    }

    [Fact]
    public void PostConfigure_NullKeyWrapperOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateSetup("us-central1")
                .PostConfigure(name: null, (GoogleCloudKmsKeyWrapperOptions)null!)
        );
    }

    [Fact]
    public void PostConfigure_NullRemoteKeyProviderOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateSetup("us-central1")
                .PostConfigure(name: null, (GoogleCloudKmsRemoteKeyProviderOptions)null!)
        );
    }

    #endregion
}
