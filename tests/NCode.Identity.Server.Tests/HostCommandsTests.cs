// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Server.InstanceSetup;
using NCode.Identity.Server.Recovery;
using NCode.Identity.Server.SecretProtection;
using Xunit;

namespace NCode.Identity.Server.Tests;

public class HostCommandsTests : BaseTests
{
    #region Select Tests

    [Fact]
    public void Select_SecretProtection_SelectsTheCommandWithNoHostArguments()
    {
        var selection = HostCommands.Select(["secret-protection", "rewrap", "--dry-run"]);

        Assert.IsType<SecretProtectionCommandLine>(selection.Command);
        Assert.Empty(selection.HostArguments);
    }

    [Fact]
    public void Select_Setup_SelectsTheCommandWithNoHostArguments()
    {
        var selection = HostCommands.Select(["setup", "extra"]);

        Assert.IsType<InstanceSetupCommandLine>(selection.Command);
        Assert.Empty(selection.HostArguments);
    }

    [Fact]
    public void Select_RevokeManagementKey_SelectsTheCommandWithNoHostArguments()
    {
        var selection = HostCommands.Select(["revoke-management-key", "kid-1"]);

        Assert.IsType<RevokeManagementKeyCommandLine>(selection.Command);
        Assert.Empty(selection.HostArguments);
    }

    [Fact]
    public void Select_RecoverAdmin_SelectsTheCommandWithNoHostArguments()
    {
        var selection = HostCommands.Select(["recover-admin"]);

        Assert.IsType<RecoverAdminCommandLine>(selection.Command);
        Assert.Empty(selection.HostArguments);
    }

    [Fact]
    public void Select_RecoverSignIn_SelectsTheCommandWithNoHostArguments()
    {
        var selection = HostCommands.Select(["recover-sign-in"]);

        Assert.IsType<RecoverSignInCommandLine>(selection.Command);
        Assert.Empty(selection.HostArguments);
    }

    [Fact]
    public void Select_OtherArguments_SelectsNoCommandAndReturnsTheArguments()
    {
        string[] args = ["--urls", "http://localhost"];

        var selection = HostCommands.Select(args);

        Assert.Null(selection.Command);
        Assert.Same(args, selection.HostArguments);
    }

    [Fact]
    public void Select_NoArguments_SelectsNoCommand()
    {
        var selection = HostCommands.Select([]);

        Assert.Null(selection.Command);
        Assert.Empty(selection.HostArguments);
    }

    [Fact]
    public void Select_ACommandNameAfterTheFirstArgument_SelectsNoCommand()
    {
        string[] args = ["--urls", "setup"];

        var selection = HostCommands.Select(args);

        Assert.Null(selection.Command);
        Assert.Same(args, selection.HostArguments);
    }

    [Fact]
    public void Select_NullArguments_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HostCommands.Select(null!));
    }

    #endregion
}
