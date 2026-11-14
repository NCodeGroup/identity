// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.SharedState;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryChangeFeedServiceTests : BaseTests
{
    private static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(10);

    private static TimeSpan PollInterval { get; } = TimeSpan.FromSeconds(30);

    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private Mock<ITeamDirectory> TeamDirectoryMock { get; }

    private Mock<ITeamDocumentApplier> TeamDocumentApplierMock { get; }

    private Mock<IPlatformDocumentApplier> PlatformDocumentApplierMock { get; }

    private Mock<IExpiringKeySet> ExpiringKeySetMock { get; }

    public DirectoryChangeFeedServiceTests()
    {
        TeamDirectoryMock = CreateStrictMock<ITeamDirectory>();
        TeamDocumentApplierMock = CreateStrictMock<ITeamDocumentApplier>();
        PlatformDocumentApplierMock = CreateStrictMock<IPlatformDocumentApplier>();
        ExpiringKeySetMock = CreateStrictMock<IExpiringKeySet>();
    }

    private DirectoryChangeFeedService CreateService(bool isControlPlane = false) =>
        new(
            TeamDirectoryMock.Object,
            TeamDocumentApplierMock.Object,
            PlatformDocumentApplierMock.Object,
            ExpiringKeySetMock.Object,
            Options.Create(new CellOptions { IsControlPlane = isControlPlane }),
            Options.Create(new DirectoryClientOptions { PollInterval = PollInterval }),
            TimeProvider,
            NullLogger<DirectoryChangeFeedService>.Instance
        );

    private void SetupLease(bool acquired) =>
        ExpiringKeySetMock
            .Setup(x =>
                x.TryAddAsync(
                    DirectoryChangeFeedService.LeaseKey,
                    TimeProvider.GetUtcNow() + PollInterval / 2,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(acquired)
            .Verifiable(Times.Once);

    private void SetupAppliedSequences(long teamSequence, long platformSequence)
    {
        TeamDocumentApplierMock
            .Setup(x => x.GetAppliedSequenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamSequence)
            .Verifiable(Times.Once);
        PlatformDocumentApplierMock
            .Setup(x => x.GetAppliedSequenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformSequence)
            .Verifiable(Times.Once);
    }

    private void SetupPage(long after, params DirectoryChange[] changes) =>
        TeamDirectoryMock
            .Setup(x =>
                x.ListChangesAsync(
                    after,
                    DirectoryChangeFeedService.PageSize,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(changes)
            .Verifiable(Times.Once);

    private static DirectoryChange TeamChange(long sequence, TeamDocument document) =>
        new()
        {
            Sequence = sequence,
            TeamId = document.TeamId,
            Document = document,
        };

    private static TeamDocument CreateTeamDocument(string teamId, long sequence) =>
        new()
        {
            TeamId = teamId,
            DisplayName = teamId,
            Sequence = sequence,
        };

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_WhenThePageIsFull_PagesUntilAnEmptyPage()
    {
        var first = CreateTeamDocument("team-a", 8);
        var second = CreateTeamDocument("team-b", 9);
        var third = CreateTeamDocument("team-c", 12);

        SetupLease(true);
        SetupAppliedSequences(teamSequence: 7, platformSequence: 5);
        SetupPage(7, TeamChange(8, first), TeamChange(9, second));
        SetupPage(9, TeamChange(12, third));
        SetupPage(12);
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(first, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(second, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(third, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenTheChangeIsAPlatformChange_AppliesThePlatformDocument()
    {
        var platform = new PlatformDocument { SettingsJson = "{}", Sequence = 4 };

        SetupLease(true);
        SetupAppliedSequences(teamSequence: 0, platformSequence: 3);
        SetupPage(3, new DirectoryChange { Sequence = 4, Platform = platform });
        SetupPage(4);
        PlatformDocumentApplierMock
            .Setup(x => x.ApplyAsync(platform, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenATeamChangeHasNoDocument_RetiresTheTeam()
    {
        SetupLease(true);
        SetupAppliedSequences(teamSequence: 2, platformSequence: 0);
        SetupPage(2, new DirectoryChange { Sequence = 3, TeamId = "team-a" });
        SetupPage(3);
        TeamDocumentApplierMock
            .Setup(x => x.RetireAsync("team-a", 3, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenTheLeaseIsHeld_DoesNothing()
    {
        SetupLease(false);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenTheDirectoryIsUnavailable_DoesNotThrow()
    {
        SetupLease(true);
        SetupAppliedSequences(teamSequence: 0, platformSequence: 0);
        TeamDirectoryMock
            .Setup(x =>
                x.ListChangesAsync(
                    0,
                    DirectoryChangeFeedService.PageSize,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() => throw new DirectoryUnavailableException("The directory is unavailable."))
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenAnApplierFails_DoesNotThrow()
    {
        var document = CreateTeamDocument("team-a", 1);

        SetupLease(true);
        SetupAppliedSequences(teamSequence: 0, platformSequence: 0);
        SetupPage(0, TeamChange(1, document));
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(document, It.IsAny<CancellationToken>()))
            .Returns(() => throw new InvalidOperationException("The row cannot be written."))
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WhenTheCellIsTheControlPlane_SkipsEverything()
    {
        using var service = CreateService(isControlPlane: true);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(Timeout);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenTheIntervalElapses_RunsAnotherPass()
    {
        var passes = 0;
        var secondPass = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        ExpiringKeySetMock
            .Setup(x =>
                x.TryAddAsync(
                    DirectoryChangeFeedService.LeaseKey,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() =>
            {
                if (Interlocked.Increment(ref passes) == 2)
                    secondPass.TrySetResult();
                return ValueTask.FromResult(false);
            })
            .Verifiable(Times.AtLeast(2));

        using var service = CreateService();

        await service.StartAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource(Timeout);
        while (!secondPass.Task.IsCompleted)
        {
            TimeProvider.Advance(PollInterval);
            await Task.Delay(1, cts.Token);
        }

        await service.StopAsync(CancellationToken.None);
    }

    #endregion
}
