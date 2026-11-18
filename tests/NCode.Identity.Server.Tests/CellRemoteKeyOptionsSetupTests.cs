// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.Secrets.Persistence.Options;
using Xunit;

namespace NCode.Identity.Server.Tests;

public sealed class CellRemoteKeyOptionsSetupTests
{
    private static CellRemoteKeyOptionsSetup CreateSetup(string cellName = "eu-1") =>
        new(Options.Create(new CellOptions { Name = cellName }));

    #region PostConfigure Tests

    [Fact]
    public void PostConfigure_WhenNoDeploymentIsConfigured_SetsTheDeploymentToTheCellName()
    {
        var options = new SecretProtectionOptions();

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal("eu-1", options.RemoteKeys.Deployment);
    }

    [Fact]
    public void PostConfigure_WhenTheDeploymentIsEmpty_SetsTheDeploymentToTheCellName()
    {
        var options = new SecretProtectionOptions { RemoteKeys = { Deployment = string.Empty } };

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal("eu-1", options.RemoteKeys.Deployment);
    }

    [Fact]
    public void PostConfigure_WhenTheDeploymentIsConfigured_KeepsIt()
    {
        var options = new SecretProtectionOptions { RemoteKeys = { Deployment = "other" } };

        CreateSetup().PostConfigure(name: null, options);

        Assert.Equal("other", options.RemoteKeys.Deployment);
    }

    [Fact]
    public void PostConfigure_WhenTheCellHasNoName_LeavesTheDeploymentUnset()
    {
        var options = new SecretProtectionOptions();

        CreateSetup(cellName: string.Empty).PostConfigure(name: null, options);

        Assert.Null(options.RemoteKeys.Deployment);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WhenTheDeploymentIsUnset_Succeeds()
    {
        var result = CreateSetup().Validate(name: null, new SecretProtectionOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenTheDeploymentEqualsTheCellName_Succeeds()
    {
        var options = new SecretProtectionOptions { RemoteKeys = { Deployment = "eu-1" } };

        var result = CreateSetup().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenTheDeploymentDiffersFromTheCellName_FailsNamingBothKeys()
    {
        var options = new SecretProtectionOptions { RemoteKeys = { Deployment = "other" } };

        var result = CreateSetup().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "SecretProtection:RemoteKeys:Deployment 'other'",
            result.FailureMessage,
            StringComparison.Ordinal
        );
        Assert.Contains("Cell:Name 'eu-1'", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenTheDeploymentIsPostConfigured_Succeeds()
    {
        var setup = CreateSetup();
        var options = new SecretProtectionOptions();

        setup.PostConfigure(name: null, options);
        var result = setup.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    #endregion
}
