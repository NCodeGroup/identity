// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.SharedState.Options;
using NCode.Identity.SharedState.Valkey.Options;
using Xunit;

namespace NCode.Identity.SharedState.Valkey.Tests.Options;

public sealed class ValkeyDeploymentOptionsSetupTests
{
    private static ValkeyDeploymentOptionsSetup CreateSetup(string deployment) =>
        new(
            Microsoft.Extensions.Options.Options.Create(
                new SharedStateOptions { Deployment = deployment }
            )
        );

    #region PostConfigure Tests

    [Fact]
    public void PostConfigure_DefaultPrefix_AppendsTheDeployment()
    {
        var options = new ValkeyOptions();

        CreateSetup("cell-a").PostConfigure(name: null, options);

        Assert.Equal("ncode-identity:cell-a:", options.KeyPrefix);
    }

    [Fact]
    public void PostConfigure_ConfiguredPrefix_KeepsItAndAppendsTheDeployment()
    {
        var options = new ValkeyOptions { KeyPrefix = "acme:" };

        CreateSetup("cell-a").PostConfigure(name: null, options);

        Assert.Equal("acme:cell-a:", options.KeyPrefix);
    }

    [Fact]
    public void PostConfigure_NoDeployment_LeavesThePrefix()
    {
        var options = new ValkeyOptions();

        CreateSetup(string.Empty).PostConfigure(name: null, options);

        Assert.Equal("ncode-identity:", options.KeyPrefix);
    }

    [Fact]
    public void PostConfigure_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateSetup("cell-a").PostConfigure(name: null, null!)
        );
    }

    #endregion
}
