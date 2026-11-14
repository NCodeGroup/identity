// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Setup.Logging;

namespace NCode.Identity.OpenId.Setup;

public sealed class InstanceSetupHostedServiceTests : IDisposable
{
    private MockRepository MockRepository { get; }
    private Mock<IInstanceSetupService> MockInstanceSetupService { get; }
    private Mock<ISetupTokenService> MockSetupTokenService { get; }
    private Mock<ILogger<InstanceSetupHostedService>> MockLogger { get; }
    private List<string> CallLog { get; } = [];

    public InstanceSetupHostedServiceTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockInstanceSetupService = MockRepository.Create<IInstanceSetupService>();
        MockSetupTokenService = MockRepository.Create<ISetupTokenService>();
        MockLogger = MockRepository.Create<ILogger<InstanceSetupHostedService>>();
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private InstanceSetupHostedService CreateService(bool isControlPlane = true) =>
        new(
            MockInstanceSetupService.Object,
            MockSetupTokenService.Object,
            Options.Create(new CellOptions { IsControlPlane = isControlPlane }),
            MockLogger.Object
        );

    private void SetupLogged(LogLevel level, int eventId)
    {
        MockLogger.Setup(x => x.IsEnabled(level)).Returns(true).Verifiable(Times.Once());
#pragma warning disable CA1873 // The arguments are matchers of a mock setup, not log arguments.
        MockLogger
            .Setup(x =>
                x.Log(
                    level,
                    It.Is<EventId>(id => id.Id == eventId),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                )
            )
#pragma warning restore CA1873
            .Callback(new InvocationAction(_ => CallLog.Add("log")))
            .Verifiable(Times.Once());
    }

    private void SetupIsInitialized(bool isInitialized)
    {
        MockInstanceSetupService
            .Setup(x => x.IsInitializedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(isInitialized)
            .Verifiable(Times.Once());
    }

    private void SetupEnsureToken()
    {
        MockSetupTokenService
            .Setup(x => x.EnsureTokenAsync(It.IsAny<CancellationToken>()))
            .Callback(() => CallLog.Add("token"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once());
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WhenTheCellIsNotTheControlPlane_LogsOnceAndPreparesNothing()
    {
        SetupLogged(LogLevel.Information, EventIds.SetupBelongsToTheControlPlane);

        await CreateService(isControlPlane: false).StartAsync(CancellationToken.None);

        Assert.Equal(["log"], CallLog);
    }

    [Fact]
    public async Task StartAsync_WhenInitialized_DoesNotEnsureToken()
    {
        SetupIsInitialized(true);

        await CreateService().StartAsync(CancellationToken.None);

        Assert.Empty(CallLog);
    }

    [Fact]
    public async Task StartAsync_WhenUninitialized_EnsuresTokenOnce()
    {
        SetupIsInitialized(false);
        SetupEnsureToken();

        await CreateService().StartAsync(CancellationToken.None);

        Assert.Equal(["token"], CallLog);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_Always_Completes()
    {
        await CreateService().StopAsync(CancellationToken.None);

        Assert.Empty(CallLog);
    }

    #endregion
}
