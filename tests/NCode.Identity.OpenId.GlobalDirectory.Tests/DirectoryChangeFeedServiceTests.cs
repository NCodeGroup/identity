// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
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

    private void SetupLease(bool acquired)
    {
        var window = TimeProvider.GetUtcNow().UtcTicks / PollInterval.Ticks;
        ExpiringKeySetMock
            .Setup(x =>
                x.TryAddAsync(
                    $"{DirectoryChangeFeedService.LeasePurpose}:{window}",
                    new DateTimeOffset((window + 1) * PollInterval.Ticks, TimeSpan.Zero),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(acquired)
            .Verifiable(Times.Once);
    }

    private static bool IsWindowLeaseKey(string key) =>
        key.StartsWith($"{DirectoryChangeFeedService.LeasePurpose}:", StringComparison.Ordinal);

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

    private void SetupReconcile(Times times) =>
        TeamDocumentApplierMock
            .Setup(x => x.ReconcileRootKeysAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(times);

    private void SetupEmptyPasses(int count)
    {
        ExpiringKeySetMock
            .Setup(x =>
                x.TryAddAsync(
                    It.Is<string>(key => IsWindowLeaseKey(key)),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable(Times.Exactly(count));
        TeamDocumentApplierMock
            .Setup(x => x.GetAppliedSequenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0)
            .Verifiable(Times.Exactly(count));
        PlatformDocumentApplierMock
            .Setup(x => x.GetAppliedSequenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0)
            .Verifiable(Times.Exactly(count));
        TeamDirectoryMock
            .Setup(x =>
                x.ListChangesAsync(
                    0,
                    DirectoryChangeFeedService.PageSize,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([])
            .Verifiable(Times.Exactly(count));
    }

    private static TeamDocument CreateTeamDocument(string teamId, long sequence) =>
        new()
        {
            TeamId = teamId,
            DisplayName = teamId,
            IsDisabled = false,
            Sequence = sequence,
            Settings = JsonElement.Parse("{}"),
            RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
        };

    #region RunAsync Tests

    [Fact]
    public async Task RunAsync_TakesALeaseKeyedByThePollWindowThatExpiresAtTheWindowsEnd()
    {
        var windowStart = TimeProvider.GetUtcNow();
        var window = windowStart.UtcTicks / PollInterval.Ticks;
        TimeProvider.Advance(TimeSpan.FromSeconds(10));
        ExpiringKeySetMock
            .Setup(x =>
                x.TryAddAsync(
                    $"{DirectoryChangeFeedService.LeasePurpose}:{window}",
                    new DateTimeOffset(2030, 1, 1, 0, 0, 30, TimeSpan.Zero),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false)
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

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
        SetupReconcile(Times.Once());
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
        var platform = new PlatformDocument { Settings = JsonElement.Parse("{}"), Sequence = 4 };

        SetupLease(true);
        SetupAppliedSequences(teamSequence: 0, platformSequence: 3);
        SetupPage(3, new DirectoryChange { Sequence = 4, Platform = platform });
        SetupPage(4);
        SetupReconcile(Times.Once());
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
        SetupReconcile(Times.Once());
        TeamDocumentApplierMock
            .Setup(x => x.RetireAsync("team-a", 3, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenTheRootKeyReconcileIntervalHasNotPassed_DoesNotReconcileAgain()
    {
        SetupEmptyPasses(3);
        SetupReconcile(Times.Exactly(2));

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
        TeamDocumentApplierMock.Verify(
            x => x.ReconcileRootKeysAsync(It.IsAny<CancellationToken>()),
            Times.Once
        );

        TimeProvider.Advance(DirectoryChangeFeedService.RootKeyReconcileInterval / 2);
        await service.RunAsync(CancellationToken.None);
        TeamDocumentApplierMock.Verify(
            x => x.ReconcileRootKeysAsync(It.IsAny<CancellationToken>()),
            Times.Once
        );

        TimeProvider.Advance(DirectoryChangeFeedService.RootKeyReconcileInterval / 2);
        await service.RunAsync(CancellationToken.None);
        TeamDocumentApplierMock.Verify(
            x => x.ReconcileRootKeysAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
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
    public async Task RunAsync_WhenAnApplierFails_ReconcilesTheRootKeys()
    {
        var document = CreateTeamDocument("team-a", 1);

        SetupLease(true);
        SetupAppliedSequences(teamSequence: 0, platformSequence: 0);
        SetupPage(0, TeamChange(1, document));
        SetupReconcile(Times.Once());
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(document, It.IsAny<CancellationToken>()))
            .Returns(() => throw new InvalidOperationException("The row cannot be written."))
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenAChangeFails_DoesNotApplyTheChangesAfterIt()
    {
        var first = CreateTeamDocument("team-a", 1);
        var second = CreateTeamDocument("team-b", 2);

        SetupLease(true);
        SetupAppliedSequences(teamSequence: 0, platformSequence: 0);
        SetupPage(0, TeamChange(1, first), TeamChange(2, second));
        SetupReconcile(Times.Once());
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(first, It.IsAny<CancellationToken>()))
            .Returns(() => throw new InvalidOperationException("The row cannot be written."))
            .Verifiable(Times.Once);
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(second, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Never);

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
                    It.Is<string>(key => IsWindowLeaseKey(key)),
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
