// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.Persistence.Stores;
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

    private Mock<IDirectoryFeedCheckpoint> FeedCheckpointMock { get; }

    private Mock<IExpiringKeySet> ExpiringKeySetMock { get; }

    public DirectoryChangeFeedServiceTests()
    {
        TeamDirectoryMock = CreateStrictMock<ITeamDirectory>();
        TeamDocumentApplierMock = CreateStrictMock<ITeamDocumentApplier>();
        PlatformDocumentApplierMock = CreateStrictMock<IPlatformDocumentApplier>();
        FeedCheckpointMock = CreateStrictMock<IDirectoryFeedCheckpoint>();
        ExpiringKeySetMock = CreateStrictMock<IExpiringKeySet>();
    }

    private DirectoryChangeFeedService CreateService(bool isControlPlane = false) =>
        new(
            TeamDirectoryMock.Object,
            TeamDocumentApplierMock.Object,
            PlatformDocumentApplierMock.Object,
            FeedCheckpointMock.Object,
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

    private void SetupLeases(int count) =>
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

    private void SetupAppliedSequence(long sequence) =>
        FeedCheckpointMock
            .Setup(x => x.GetAppliedSequenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sequence)
            .Verifiable(Times.Once);

    private void SetupRecordedSequence(long sequence) =>
        FeedCheckpointMock
            .Setup(x => x.RecordAppliedSequenceAsync(sequence, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

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
        SetupLeases(count);
        FeedCheckpointMock
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
    public async Task RunAsync_Always_TakesALeaseKeyedByThePollWindowThatExpiresAtItsEnd()
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
        SetupAppliedSequence(7);
        SetupPage(7, TeamChange(8, first), TeamChange(9, second));
        SetupPage(9, TeamChange(12, third));
        SetupPage(12);
        SetupRecordedSequence(9);
        SetupRecordedSequence(12);
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
        SetupAppliedSequence(3);
        SetupPage(3, new DirectoryChange { Sequence = 4, Platform = platform });
        SetupPage(4);
        SetupRecordedSequence(4);
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
        SetupAppliedSequence(2);
        SetupPage(2, new DirectoryChange { Sequence = 3, TeamId = "team-a" });
        SetupPage(3);
        SetupRecordedSequence(3);
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
        SetupAppliedSequence(0);
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
        SetupAppliedSequence(0);
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
        SetupAppliedSequence(0);
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

    [Fact]
    public async Task RunAsync_WhenAPageApplies_RecordsItsLastSequence()
    {
        var first = CreateTeamDocument("team-a", 4);
        var second = CreateTeamDocument("team-b", 6);
        var third = CreateTeamDocument("team-c", 9);

        SetupLease(true);
        SetupAppliedSequence(3);
        SetupPage(3, TeamChange(4, first), TeamChange(6, second), TeamChange(9, third));
        SetupPage(9);
        SetupRecordedSequence(9);
        SetupReconcile(Times.Once());
        foreach (var document in new[] { first, second, third })
        {
            TeamDocumentApplierMock
                .Setup(x => x.ApplyAsync(document, It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask)
                .Verifiable(Times.Once);
        }

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenAChangeFailsAfterOthersApplied_RecordsTheLastApplied()
    {
        var first = CreateTeamDocument("team-a", 1);
        var second = CreateTeamDocument("team-b", 2);
        var third = CreateTeamDocument("team-c", 3);

        SetupLease(true);
        SetupAppliedSequence(0);
        SetupPage(0, TeamChange(1, first), TeamChange(2, second), TeamChange(3, third));
        SetupRecordedSequence(2);
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
            .Returns(() => throw new InvalidOperationException("The row cannot be written."))
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenAChangeLosesAConcurrencyRace_AppliesItAgain()
    {
        var document = CreateTeamDocument("team-a", 1);
        var attempts = 0;

        SetupLease(true);
        SetupAppliedSequence(0);
        SetupPage(0, TeamChange(1, document));
        SetupPage(1);
        SetupRecordedSequence(1);
        SetupReconcile(Times.Once());
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(document, It.IsAny<CancellationToken>()))
            .Returns(() =>
                ++attempts < 2
                    ? ValueTask.FromException(new StoreConcurrencyException("The row changed."))
                    : ValueTask.CompletedTask
            )
            .Verifiable(Times.Exactly(2));

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RunAsync_WhenAChangeLosesThreeRaces_StopsThePassAtIt()
    {
        var first = CreateTeamDocument("team-a", 1);
        var second = CreateTeamDocument("team-b", 2);

        SetupLease(true);
        SetupAppliedSequence(0);
        SetupPage(0, TeamChange(1, first), TeamChange(2, second));
        SetupReconcile(Times.Once());
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(first, It.IsAny<CancellationToken>()))
            .Returns(() =>
                ValueTask.FromException(new StoreConcurrencyException("The row changed."))
            )
            .Verifiable(Times.Exactly(DirectoryChangeFeedService.ConcurrencyAttempts));
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(second, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Never);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenTheNewestChangeIsARetirement_DoesNotApplyItAgain()
    {
        var recorded = 2L;

        SetupLeases(2);
        SetupPage(2, new DirectoryChange { Sequence = 3, TeamId = "team-a" });
        SetupReconcile(Times.Once());
        TeamDirectoryMock
            .Setup(x =>
                x.ListChangesAsync(
                    3,
                    DirectoryChangeFeedService.PageSize,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([])
            .Verifiable(Times.Exactly(2));
        FeedCheckpointMock
            .Setup(x => x.GetAppliedSequenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => recorded)
            .Verifiable(Times.Exactly(2));
        FeedCheckpointMock
            .Setup(x => x.RecordAppliedSequenceAsync(3, It.IsAny<CancellationToken>()))
            .Callback<long, CancellationToken>((sequence, _) => recorded = sequence)
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        TeamDocumentApplierMock
            .Setup(x => x.RetireAsync("team-a", 3, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
        TimeProvider.Advance(PollInterval);
        await service.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_WhenAChildPrecedesItsParent_AppliesBothWhenTheDirectoryListsThemAgain()
    {
        var child = CreateTeamDocument("team-c", 3) with { ParentTeamId = "team-p" };
        var parent = CreateTeamDocument("team-p", 5);
        var childAgain = CreateTeamDocument("team-c", 6) with { ParentTeamId = "team-p" };
        var recorded = 2L;
        var listings = 0;

        SetupLeases(2);
        SetupReconcile(Times.Once());
        SetupPage(2, TeamChange(3, child));
        SetupPage(6);
        TeamDirectoryMock
            .Setup(x =>
                x.ListChangesAsync(
                    3,
                    DirectoryChangeFeedService.PageSize,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(() =>
                ++listings < 2
                    ? (IReadOnlyList<DirectoryChange>)[]
                    : [TeamChange(5, parent), TeamChange(6, childAgain)]
            )
            .Verifiable(Times.Exactly(2));
        FeedCheckpointMock
            .Setup(x => x.GetAppliedSequenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => recorded)
            .Verifiable(Times.Exactly(2));
        FeedCheckpointMock
            .Setup(x =>
                x.RecordAppliedSequenceAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())
            )
            .Callback<long, CancellationToken>((sequence, _) => recorded = sequence)
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Exactly(2));
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(child, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(parent, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        TeamDocumentApplierMock
            .Setup(x => x.ApplyAsync(childAgain, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        using var service = CreateService();

        await service.RunAsync(CancellationToken.None);
        TimeProvider.Advance(PollInterval);
        await service.RunAsync(CancellationToken.None);

        Assert.Equal(6, recorded);
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
