// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryOutboxPublisherTests : BaseTests
{
    private static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(10);

    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private Mock<IPlatformTopology> TopologyMock { get; }
    private Mock<IDirectoryOutbox> OutboxMock { get; }
    private Mock<IDirectoryOutboxSignal> SignalMock { get; }
    private Mock<IReplicatedDocumentDirectory> DirectoryMock { get; }
    private DirectoryOutboxPublisher Publisher { get; }

    public DirectoryOutboxPublisherTests()
    {
        TopologyMock = CreateStrictMock<IPlatformTopology>();
        OutboxMock = CreateStrictMock<IDirectoryOutbox>();
        SignalMock = CreateStrictMock<IDirectoryOutboxSignal>();
        DirectoryMock = CreateStrictMock<IReplicatedDocumentDirectory>();
        Publisher = new DirectoryOutboxPublisher(
            TopologyMock.Object,
            OutboxMock.Object,
            SignalMock.Object,
            DirectoryMock.Object,
            Options.Create(new DirectoryClientOptions()),
            TimeProvider,
            new TestMeterFactory(),
            NullLogger<DirectoryOutboxPublisher>.Instance
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Publisher.Dispose();

        base.Dispose(disposing);
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose() { }
    }

    private static ReplicatedDocument Document(string kind, string key, long revision) =>
        new()
        {
            Kind = kind,
            Key = key,
            Revision = revision,
            Content = JsonSerializer.SerializeToElement(new { }),
        };

    private static ReplicatedDocument Revision(string kind, string key, long revision) =>
        new()
        {
            Kind = kind,
            Key = key,
            Revision = revision,
        };

    [Fact]
    public async Task StartAsync_WhenNotTheControlPlaneCell_DoesNothing()
    {
        TopologyMock.Setup(x => x.IsControlPlaneCell).Returns(false).Verifiable(Times.Once);

        await Publisher.StartAsync(CancellationToken.None);
        await Publisher.ExecuteTask!.WaitAsync(Timeout);
        await Publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DrainAsync_WhenRowsWaitAfterACrash_SendsTheNewestRowAndAcknowledgesIt()
    {
        var kind = ReplicatedDocumentKinds.PlatformAuthority;
        var older = Document(kind, "", 3);
        var newest = Document(kind, "", 4);
        OutboxMock
            .Setup(x =>
                x.ListPendingAsync(DirectoryOutboxPublisher.PageSize, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([older, newest])
            .Verifiable(Times.Once);
        DirectoryMock
            .Setup(x => x.PublishAsync(newest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4)
            .Verifiable(Times.Once);
        OutboxMock
            .Setup(x => x.AcknowledgeAsync(kind, "", 4, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        var sent = await Publisher.DrainAsync(CancellationToken.None);

        Assert.Equal(1, sent);
    }

    [Fact]
    public async Task DrainAsync_WhenADocumentIsSent_RecordsThePublishedCounterWithTheDocumentKind()
    {
        var kind = ReplicatedDocumentKinds.PlatformAuthority;
        var newest = Document(kind, "", 4);
        var tags = new List<KeyValuePair<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == DirectoryOutboxPublisher.PublishedCounterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>(
            (_, _, measurementTags, _) => tags.AddRange(measurementTags.ToArray())
        );
        listener.Start();
        OutboxMock
            .Setup(x =>
                x.ListPendingAsync(DirectoryOutboxPublisher.PageSize, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([newest])
            .Verifiable(Times.Once);
        DirectoryMock
            .Setup(x => x.PublishAsync(newest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4)
            .Verifiable(Times.Once);
        OutboxMock
            .Setup(x => x.AcknowledgeAsync(kind, "", 4, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        await Publisher.DrainAsync(CancellationToken.None);

        var tag = Assert.Single(tags);
        Assert.Equal("ncode.identity.directory.document.kind", tag.Key);
        Assert.Equal(kind, tag.Value);
    }

    [Fact]
    public async Task DrainAsync_WhenTheFirstDocumentFails_SendsTheSecondAndLeavesTheFirstPending()
    {
        var kind = ReplicatedDocumentKinds.TeamAuthority;
        var first = Document(kind, "team-a", 1);
        var second = Document(kind, "team-b", 2);
        OutboxMock
            .Setup(x =>
                x.ListPendingAsync(DirectoryOutboxPublisher.PageSize, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([first, second])
            .Verifiable(Times.Once);
        DirectoryMock
            .Setup(x => x.PublishAsync(first, It.IsAny<CancellationToken>()))
            .Returns(() => throw new DirectoryUnavailableException("The document is refused."))
            .Verifiable(Times.Once);
        DirectoryMock
            .Setup(x => x.PublishAsync(second, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2)
            .Verifiable(Times.Once);
        OutboxMock
            .Setup(x => x.AcknowledgeAsync(kind, "team-b", 2, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await Publisher.DrainAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task DrainAsync_WhenTheDirectoryHoldsAHigherRevision_AcknowledgesThroughItAndRepublishesAboveIt()
    {
        var kind = ReplicatedDocumentKinds.TeamAuthority;
        var pending = Document(kind, "team-a", 2);
        OutboxMock
            .Setup(x =>
                x.ListPendingAsync(DirectoryOutboxPublisher.PageSize, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([pending])
            .Verifiable(Times.Once);
        DirectoryMock
            .Setup(x => x.PublishAsync(pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7)
            .Verifiable(Times.Once);
        OutboxMock
            .Setup(x => x.AcknowledgeAsync(kind, "team-a", 7, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        OutboxMock
            .Setup(x => x.RepublishAsync(kind, "team-a", 8, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        var sent = await Publisher.DrainAsync(CancellationToken.None);

        Assert.Equal(1, sent);
    }

    [Fact]
    public async Task RepublishBehindAsync_WhenTheLocalRevisionIsBehindAcrossTwoPages_RepublishesAboveTheDirectorys()
    {
        var platform = ReplicatedDocumentKinds.PlatformAuthority;
        var keys = ReplicatedDocumentKinds.ManagementKeys;
        DirectoryMock
            .Setup(x =>
                x.ListRevisionsAsync(
                    null,
                    DirectoryOutboxPublisher.PageSize,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new ReplicatedRevisionPage
                {
                    Revisions = [Revision(platform, "", 9)],
                    Continuation = "next",
                }
            )
            .Verifiable(Times.Once);
        DirectoryMock
            .Setup(x =>
                x.ListRevisionsAsync(
                    "next",
                    DirectoryOutboxPublisher.PageSize,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new ReplicatedRevisionPage { Revisions = [Revision(keys, "", 2)] })
            .Verifiable(Times.Once);
        OutboxMock
            .Setup(x => x.ListLocalRevisionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Revision(platform, "", 3), Revision(keys, "", 2)])
            .Verifiable(Times.Once);
        OutboxMock
            .Setup(x => x.RepublishAsync(platform, "", 10, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        await Publisher.RepublishBehindAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenTheDirectoryIsUnavailable_RetriesAfterTheDelay()
    {
        var kind = ReplicatedDocumentKinds.PlatformAuthority;
        var pending = Document(kind, "", 1);
        var calls = 0;
        TopologyMock.Setup(x => x.IsControlPlaneCell).Returns(true).Verifiable(Times.Once);
        DirectoryMock
            .Setup(x =>
                x.ListRevisionsAsync(
                    null,
                    DirectoryOutboxPublisher.PageSize,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new ReplicatedRevisionPage { Revisions = [] })
            .Verifiable(Times.Once);
        OutboxMock
            .Setup(x => x.ListLocalRevisionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .Verifiable(Times.Once);
        var published = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var acknowledged = false;
        OutboxMock
            .Setup(x =>
                x.ListPendingAsync(DirectoryOutboxPublisher.PageSize, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(() => acknowledged ? [] : [pending]);
        DirectoryMock
            .Setup(x => x.PublishAsync(pending, It.IsAny<CancellationToken>()))
            .Returns(() =>
                Interlocked.Increment(ref calls) == 1
                    ? throw new DirectoryUnavailableException("The directory is unavailable.")
                    : ValueTask.FromResult(1L)
            );
        OutboxMock
            .Setup(x => x.AcknowledgeAsync(kind, "", 1, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                acknowledged = true;
                return ValueTask.CompletedTask;
            })
            .Verifiable(Times.Once);
        SignalMock
            .Setup(x => x.WaitAsync(TimeSpan.FromSeconds(5), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                published.TrySetResult();
                return ValueTask.FromCanceled(new CancellationToken(true));
            });

        await Publisher.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(Timeout);
        while (!published.Task.IsCompleted)
        {
            TimeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1, cts.Token);
        }

        await Publisher.StopAsync(CancellationToken.None);

        Assert.Equal(2, calls);
    }
}
