// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.Secrets.Options;
using NCode.Identity.Server.Cells;
using NCode.Identity.SharedState.Options;
using Xunit;

namespace NCode.Identity.Server.Tests.Cells;

public sealed class CellScopeOptionsSetupTests
{
    private static CellScopeOptionsSetup CreateSetup(
        string cellName,
        params string[] cloudRegions
    ) => new(Options.Create(new CellOptions { Name = cellName, CloudRegions = cloudRegions }));

    #region PostConfigure Tests

    [Fact]
    public void PostConfigure_CellHasAName_MakesItTheDeployment()
    {
        var options = new SharedStateOptions();

        CreateSetup("eu-1").PostConfigure(name: null, options);

        Assert.Equal("eu-1", options.Deployment);
    }

    [Fact]
    public void PostConfigure_DeploymentConfigured_ReplacesItWithTheCellName()
    {
        var options = new SharedStateOptions { Deployment = "other" };

        CreateSetup("eu-1").PostConfigure(name: null, options);

        Assert.Equal("eu-1", options.Deployment);
    }

    [Fact]
    public void PostConfigure_CellHasNoName_LeavesTheDeployment()
    {
        var options = new SharedStateOptions { Deployment = "other" };

        CreateSetup(string.Empty).PostConfigure(name: null, options);

        Assert.Equal("other", options.Deployment);
    }

    [Fact]
    public void PostConfigure_CellNamesCloudRegions_MakesThemTheKeyResidency()
    {
        var options = new KeyResidencyOptions { CloudRegions = ["us-east-1"] };

        CreateSetup("eu-1", "eu-west-1", "eu-central-1").PostConfigure(name: null, options);

        Assert.Equal(["eu-west-1", "eu-central-1"], options.CloudRegions);
    }

    [Fact]
    public void PostConfigure_CellNamesNoCloudRegion_LeavesTheKeyResidency()
    {
        var options = new KeyResidencyOptions { CloudRegions = ["us-east-1"] };

        CreateSetup("eu-1").PostConfigure(name: null, options);

        Assert.Equal(["us-east-1"], options.CloudRegions);
    }

    [Fact]
    public void PostConfigure_NullSharedStateOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateSetup("eu-1").PostConfigure(name: null, (SharedStateOptions)null!)
        );
    }

    [Fact]
    public void PostConfigure_NullKeyResidencyOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateSetup("eu-1").PostConfigure(name: null, (KeyResidencyOptions)null!)
        );
    }

    #endregion
}
