// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class CellRegistrationServiceTests : BaseTests
{
    private static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(10);

    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private Mock<ICellDirectory> CellDirectoryMock { get; }

    private CellDescriptor ExpectedDescriptor { get; } =
        new()
        {
            Name = "cell-a",
            Region = "westeurope",
            Kind = CellKinds.Shared,
            ManagementUrl = "https://cell-a.example.test/management",
            IsControlPlane = true,
        };

    private CellRegistrationService Service { get; }

    public CellRegistrationServiceTests()
    {
        CellDirectoryMock = CreateStrictMock<ICellDirectory>();
        Service = new CellRegistrationService(
            CellDirectoryMock.Object,
            Options.Create(
                new CellOptions
                {
                    Name = ExpectedDescriptor.Name,
                    Region = ExpectedDescriptor.Region,
                    Kind = ExpectedDescriptor.Kind,
                    ManagementUrl = ExpectedDescriptor.ManagementUrl,
                    IsControlPlane = ExpectedDescriptor.IsControlPlane,
                }
            ),
            TimeProvider,
            NullLogger<CellRegistrationService>.Instance
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Service.Dispose();

        base.Dispose(disposing);
    }

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WhenTheDirectoryAccepts_RegistersTheCellOnceWithTheDescriptorFromTheOptions()
    {
        CellDirectoryMock
            .Setup(x => x.ReportAsync(ExpectedDescriptor, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        await Service.StartAsync(CancellationToken.None);
        await Service.ExecuteTask!.WaitAsync(Timeout);
        await Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenTheDirectoryIsUnavailable_RetriesAfterTheDelay()
    {
        var calls = 0;
        CellDirectoryMock
            .Setup(x => x.ReportAsync(ExpectedDescriptor, It.IsAny<CancellationToken>()))
            .Returns(() =>
                Interlocked.Increment(ref calls) == 1
                    ? throw new DirectoryUnavailableException("The directory is unavailable.")
                    : ValueTask.CompletedTask
            )
            .Verifiable(Times.Exactly(2));

        await Service.StartAsync(CancellationToken.None);

        // The first retry waits at most one second, whatever its jitter; each advance is harmless once the second
        // attempt has succeeded.
        using var cts = new CancellationTokenSource(Timeout);
        while (!Service.ExecuteTask!.IsCompleted)
        {
            TimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1, cts.Token);
        }

        await Service.ExecuteTask;
        await Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenTheHostStopsWhileWaiting_StopsWithoutThrowing()
    {
        var attempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CellDirectoryMock
            .Setup(x => x.ReportAsync(ExpectedDescriptor, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempted.TrySetResult();
                throw new DirectoryUnavailableException("The directory is unavailable.");
            })
            .Verifiable(Times.Once);

        await Service.StartAsync(CancellationToken.None);
        await attempted.Task.WaitAsync(Timeout);
        await Service.StopAsync(CancellationToken.None);

        Assert.True(Service.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task StartAsync_WhenTheDirectoryFailsOtherwise_PropagatesTheException()
    {
        CellDirectoryMock
            .Setup(x => x.ReportAsync(ExpectedDescriptor, It.IsAny<CancellationToken>()))
            .Returns(() => throw new InvalidOperationException("The cell is misconfigured."))
            .Verifiable(Times.Once);

        await Service.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Service.ExecuteTask!.WaitAsync(Timeout)
        );
    }

    #endregion

    #region GetDelay Tests

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(8, 60)]
    [InlineData(2000, 60)]
    public void GetDelay_WithTheLargestJitter_DoublesFromOneSecondUpToSixtySeconds(
        int attempt,
        int expectedSeconds
    )
    {
        var delay = CellRegistrationService.GetDelay(attempt, 0.999);

        Assert.InRange(
            delay,
            TimeSpan.FromSeconds(expectedSeconds * 0.999),
            TimeSpan.FromSeconds(expectedSeconds)
        );
    }

    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(1, 0.999)]
    [InlineData(20, 0.0)]
    [InlineData(20, 0.999)]
    public void GetDelay_WithAnyJitter_StaysBetweenHalfAndAllOfTheCappedDelay(
        int attempt,
        double jitter
    )
    {
        var capped = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 60));

        var delay = CellRegistrationService.GetDelay(attempt, jitter);

        Assert.InRange(delay, capped / 2, capped);
    }

    #endregion
}
