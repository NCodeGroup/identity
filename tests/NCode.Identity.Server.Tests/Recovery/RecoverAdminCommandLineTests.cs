// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Setup;
using NCode.Identity.Server.Recovery;
using Xunit;

namespace NCode.Identity.Server.Tests.Recovery;

public class RecoverAdminCommandLineTests : BaseTests
{
    private const string UserName = "admin";
    private const string Password = "a-recovery-password-of-15-or-more";

    private static RecoverAdminCommandLine Parse(params string[] args)
    {
        var command = RecoverAdminCommandLine.Parse(args);
        Assert.NotNull(command);
        return command;
    }

    private static ServiceProvider CreateServices(
        IAdministratorRecoveryService recoveryService,
        bool isControlPlane,
        string? userName = UserName,
        string? password = Password
    )
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(recoveryService);
        serviceCollection.AddSingleton(
            Options.Create(new CellOptions { IsControlPlane = isControlPlane })
        );
        var recoveryOptions = new RecoveryOptions
        {
            Admin = new RecoveryAdminOptions { UserName = userName, Password = password },
        };
        serviceCollection.AddSingleton(Options.Create(recoveryOptions));
        return serviceCollection.BuildServiceProvider();
    }

    #region Parse Tests

    [Theory]
    [InlineData]
    [InlineData("--urls", "http://localhost")]
    [InlineData("setup")]
    public void Parse_ArgsThatAreNotTheCommand_ReturnNull(params string[] args)
    {
        Assert.Null(RecoverAdminCommandLine.Parse(args));
    }

    [Fact]
    public void Parse_TheCommandAlone_ReturnsACommandWithoutAnError()
    {
        Assert.Null(Parse("recover-admin").Error);
    }

    [Fact]
    public void Parse_AnExtraArgument_ReturnsACommandWithAnError()
    {
        Assert.False(string.IsNullOrWhiteSpace(Parse("recover-admin", "extra").Error));
    }

    #endregion

    #region GetHostArguments Tests

    [Fact]
    public void GetHostArguments_TheCommand_ReturnsNoArguments()
    {
        Assert.Empty(RecoverAdminCommandLine.GetHostArguments(["recover-admin"]));
    }

    [Fact]
    public void GetHostArguments_OtherArguments_ReturnsThem()
    {
        string[] args = ["--urls", "http://localhost"];

        Assert.Equal(args, RecoverAdminCommandLine.GetHostArguments(args));
    }

    #endregion

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_InvalidArguments_ReturnsTwoWithoutRecovering()
    {
        var service = CreateStrictMock<IAdministratorRecoveryService>();
        using var services = CreateServices(service.Object, isControlPlane: true);

        var exitCode = await Parse("recover-admin", "extra")
            .RunAsync(services, CancellationToken.None);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_AWorkloadCell_ReturnsTwoWithoutRecovering()
    {
        var service = CreateStrictMock<IAdministratorRecoveryService>();
        using var services = CreateServices(service.Object, isControlPlane: false);

        var exitCode = await Parse("recover-admin").RunAsync(services, CancellationToken.None);

        Assert.Equal(2, exitCode);
    }

    [Theory]
    [InlineData(null, Password)]
    [InlineData(" ", Password)]
    [InlineData(UserName, null)]
    [InlineData(UserName, "")]
    public async Task RunAsync_UserNameOrPasswordMissing_ReturnsTwoWithoutRecovering(
        string? userName,
        string? password
    )
    {
        var service = CreateStrictMock<IAdministratorRecoveryService>();
        using var services = CreateServices(service.Object, true, userName, password);

        var exitCode = await Parse("recover-admin").RunAsync(services, CancellationToken.None);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_TheServiceRecovers_ReturnsZero()
    {
        var service = CreateStrictMock<IAdministratorRecoveryService>();
        service
            .Setup(x =>
                x.RecoverAsync(
                    It.Is<RecoveryAdminOptions>(options =>
                        options.UserName == UserName && options.Password == Password
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new AdministratorRecoveryResult
                {
                    TenantId = "control-plane",
                    PrincipalId = "principal-1",
                    Created = true,
                }
            )
            .Verifiable(Times.Once());
        using var services = CreateServices(service.Object, isControlPlane: true);

        var exitCode = await Parse("recover-admin").RunAsync(services, CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_TheServiceRefuses_ReturnsOne()
    {
        var service = CreateStrictMock<IAdministratorRecoveryService>();
        service
            .Setup(x =>
                x.RecoverAsync(It.IsAny<RecoveryAdminOptions>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new AdministratorRecoveryResult
                {
                    Error = new ManagementError
                    {
                        StatusCode = StatusCodes.Status409Conflict,
                        Detail = "refused",
                    },
                }
            )
            .Verifiable(Times.Once());
        using var services = CreateServices(service.Object, isControlPlane: true);

        var exitCode = await Parse("recover-admin").RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_TheServiceFails_ReturnsOne()
    {
        var service = CreateStrictMock<IAdministratorRecoveryService>();
        service
            .Setup(x =>
                x.RecoverAsync(It.IsAny<RecoveryAdminOptions>(), It.IsAny<CancellationToken>())
            )
            .Throws(new InvalidOperationException("The database is down."))
            .Verifiable(Times.Once());
        using var services = CreateServices(service.Object, isControlPlane: true);

        var exitCode = await Parse("recover-admin").RunAsync(services, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    #endregion
}
