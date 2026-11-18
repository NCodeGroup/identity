// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Secrets.AwsKms.Options;
using NCode.Identity.Secrets.Options;
using Xunit;

namespace NCode.Identity.Secrets.AwsKms.Tests.Options;

public class AwsKmsKeyResidencyOptionsSetupTests
{
    private static AwsKmsKeyResidencyOptionsSetup CreateSetup(params string[] cloudRegions) =>
        new(
            Microsoft.Extensions.Options.Options.Create(
                new KeyResidencyOptions { CloudRegions = [.. cloudRegions] }
            )
        );

    #region PostConfigure Tests

    [Fact]
    public void PostConfigure_KeyWrapperOptionsWithCloudRegions_ReplacesTheAllowedRegions()
    {
        var options = new AwsKmsKeyWrapperOptions { AllowedRegions = ["eu-west-1"] };

        CreateSetup("us-east-1", "us-west-2").PostConfigure(name: null, options);

        Assert.Equal(["us-east-1", "us-west-2"], options.AllowedRegions);
    }

    [Fact]
    public void PostConfigure_KeyWrapperOptionsWithoutCloudRegions_LeavesTheAllowedRegions()
    {
        var options = new AwsKmsKeyWrapperOptions { AllowedRegions = ["eu-west-1"] };

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal(["eu-west-1"], options.AllowedRegions);
    }

    [Fact]
    public void PostConfigure_RemoteKeyProviderOptionsWithCloudRegions_ReplacesTheAllowedRegions()
    {
        var options = new AwsKmsRemoteKeyProviderOptions { AllowedRegions = ["eu-west-1"] };

        CreateSetup("us-east-1", "us-west-2").PostConfigure(name: null, options);

        Assert.Equal(["us-east-1", "us-west-2"], options.AllowedRegions);
    }

    [Fact]
    public void PostConfigure_RemoteKeyProviderOptionsWithoutCloudRegions_LeavesTheAllowedRegions()
    {
        var options = new AwsKmsRemoteKeyProviderOptions { AllowedRegions = ["eu-west-1"] };

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal(["eu-west-1"], options.AllowedRegions);
    }

    [Fact]
    public void PostConfigure_NullKeyWrapperOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateSetup("us-east-1").PostConfigure(name: null, (AwsKmsKeyWrapperOptions)null!)
        );
    }

    [Fact]
    public void PostConfigure_NullRemoteKeyProviderOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateSetup("us-east-1")
                .PostConfigure(name: null, (AwsKmsRemoteKeyProviderOptions)null!)
        );
    }

    #endregion
}
