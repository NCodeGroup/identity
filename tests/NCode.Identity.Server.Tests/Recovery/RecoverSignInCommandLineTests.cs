// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Setup;
using NCode.Identity.Server.Recovery;
using Xunit;

namespace NCode.Identity.Server.Tests.Recovery;

public class RecoverSignInCommandLineTests : BaseTests
{
    private static RecoverSignInCommandLine Parse(params string[] args)
    {
        var command = RecoverSignInCommandLine.Parse(args);
        Assert.NotNull(command);
        return command;
    }

    private static ServiceProvider CreateServices(
        ISignInRecoveryService recoveryService,
        bool isControlPlane,
        bool hasManagementTenant
    )
    {
        var topology = new Mock<IPlatformTopology>();
        topology.SetupGet(x => x.IsControlPlaneCell).Returns(isControlPlane);
        topology.SetupGet(x => x.HasManagementTenant).Returns(hasManagementTenant);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(recoveryService);
        serviceCollection.AddSingleton(topology.Object);
        return serviceCollection.BuildServiceProvider();
    }

    #region Parse Tests

    [Theory]
    [InlineData]
    [InlineData("--urls", "http://localhost")]
    [InlineData("recover-admin")]
    public void Parse_ArgsThatAreNotTheCommand_ReturnNull(params string[] args)
    {
        Assert.Null(RecoverSignInCommandLine.Parse(args));
    }

    [Fact]
    public void Parse_TheCommandAlone_ReturnsACommandWithoutAnError()
    {
        Assert.Null(Parse("recover-sign-in").Error);
    }

    [Fact]
    public void Parse_AnExtraArgument_ReturnsACommandWithAnError()
    {
        Assert.False(string.IsNullOrWhiteSpace(Parse("recover-sign-in", "extra").Error));
    }

    #endregion

    #region GetHostArguments Tests

    [Fact]
    public void GetHostArguments_TheCommand_ReturnsNoArguments()
    {
        Assert.Empty(RecoverSignInCommandLine.GetHostArguments(["recover-sign-in"]));
    }

    [Fact]
    public void GetHostArguments_OtherArguments_ReturnsThem()
    {
        string[] args = ["--urls", "http://localhost"];

        Assert.Equal(args, RecoverSignInCommandLine.GetHostArguments(args));
    }

    #endregion

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_InvalidArguments_ReturnsTwoWithoutRepairing()
    {
        var service = CreateStrictMock<ISignInRecoveryService>();
        using var services = CreateServices(service.Object, true, false);

        var exitCode = await Parse("recover-sign-in", "extra")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_AWorkloadCellWithoutAManagementTenant_ReturnsTwoWithoutRepairing()
    {
        var service = CreateStrictMock<ISignInRecoveryService>();
        using var services = CreateServices(service.Object, false, false);

        var exitCode = await Parse("recover-sign-in").RunAsync(services, CancellationToken.None);

        Assert.Equal(2, exitCode);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunAsync_TheServiceRepairs_ReturnsZero(
        bool isControlPlane,
        bool hasManagementTenant
    )
    {
        var service = CreateStrictMock<ISignInRecoveryService>();
        service
            .Setup(x => x.RepairAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new SignInRepairResult
                {
                    Changes =
                    [
                        new SignInRepairChange
                        {
                            Scope = SignInRepairScopes.Tenant,
                            Name = "allowed_connections",
                            Before = "[]",
                        },
                    ],
                }
            )
            .Verifiable(Times.Once());
        using var services = CreateServices(service.Object, isControlPlane, hasManagementTenant);

        var exitCode = await Parse("recover-sign-in").RunAsync(services, CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_TheServiceFails_ReturnsOne()
    {
        var service = CreateStrictMock<ISignInRecoveryService>();
        service
            .Setup(x => x.RepairAsync(It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("The database is down."))
            .Verifiable(Times.Once());
        using var services = CreateServices(service.Object, true, false);

        var exitCode = await Parse("recover-sign-in").RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    #endregion
}
