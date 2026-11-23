// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using NCode.Identity.Dispatch;
using NCode.Identity.Mail.Logging;
using Xunit;

namespace NCode.Identity.Mail.Tests;

public class MailDeliveryJobHandlerTests : BaseTests
{
    private const string Recipient = "user@example.com";
    private const string BodyText = "Your code is 123456";

    private sealed record CodeModel(string To, int Code);

    private static CodeModel Model { get; } = new(Recipient, 123456);

    private static MailDeliveryJob Job { get; } = new(Model);

    private static DispatchContext Context { get; } =
        new()
        {
            DispatchId = Guid.NewGuid(),
            Attempt = 2,
            MaxAttempts = 4,
        };

    private Mock<IMailComposer> MockComposer { get; }

    private Mock<IMailTransport> MockTransport { get; }

    private ServiceProvider MetricsProvider { get; }

    private MetricCollector<long> MessagesCollector { get; }

    private FakeLogger<MailDeliveryJobHandler> Logger { get; } = new();

    private MailDeliveryJobHandler Handler { get; }

    public MailDeliveryJobHandlerTests()
    {
        MockComposer = CreateStrictMock<IMailComposer>();
        MockComposer.Setup(x => x.ModelType).Returns(typeof(CodeModel)).Verifiable();

        MockTransport = CreateStrictMock<IMailTransport>();

        MetricsProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = MetricsProvider.GetRequiredService<IMeterFactory>();
        MessagesCollector = new MetricCollector<long>(
            meterFactory,
            MailMetrics.MeterName,
            MailDeliveryMetrics.MessagesName
        );

        var metrics = new MailDeliveryMetrics(meterFactory);
        Handler = new MailDeliveryJobHandler(
            new MailComposerRegistry([MockComposer.Object]),
            MockTransport.Object,
            metrics,
            Logger
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            MessagesCollector.Dispose();
            MetricsProvider.Dispose();
        }

        base.Dispose(disposing);
    }

    private static (MailMessage Message, MemoryStream Stream) CreateMessage()
    {
        var stream = new MemoryStream("Your code is 123456"u8.ToArray());
        var message = new MailMessage
        {
            To = [new MailAddress(Recipient)],
            Subject = "Subject",
            TextBody = new StreamMailContent(stream, "text/plain", "utf-8"),
        };
        return (message, stream);
    }

    #region HandleAsync Tests

    [Fact]
    public async Task HandleAsync_WhenTheTransportDelivers_CountsADeliveredMessageAndDisposesIt()
    {
        var (message, stream) = CreateMessage();
        MockComposer
            .Setup(x => x.ComposeAsync(Model, CancellationToken.None))
            .ReturnsAsync(message)
            .Verifiable();
        MockTransport
            .Setup(x => x.DeliverAsync(message, CancellationToken.None))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await Handler.HandleAsync(Job, Context, CancellationToken.None);

        var measurement = Assert.Single(MessagesCollector.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(
            MailDeliveryMetrics.DeliveredResult,
            measurement.Tags[MailDeliveryMetrics.ResultAttribute]
        );
        Assert.Empty(Logger.Collector.GetSnapshot());
        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task HandleAsync_WhenTheTransportThrows_CountsAFailureLogsRethrowsAndDisposesTheMessage()
    {
        var (message, stream) = CreateMessage();
        var failure = new InvalidOperationException("The provider refused the message.");
        MockComposer
            .Setup(x => x.ComposeAsync(Model, CancellationToken.None))
            .ReturnsAsync(message)
            .Verifiable();
        MockTransport
            .Setup(x => x.DeliverAsync(message, CancellationToken.None))
            .Throws(failure)
            .Verifiable();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Handler.HandleAsync(Job, Context, CancellationToken.None)
        );

        Assert.Same(failure, thrown);
        Assert.False(stream.CanRead);

        var measurement = Assert.Single(MessagesCollector.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(
            MailDeliveryMetrics.FailedResult,
            measurement.Tags[MailDeliveryMetrics.ResultAttribute]
        );

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(EventIds.MailDeliveryFailed, record.Id.Id);
        Assert.Same(failure, record.Exception);
        Assert.Contains("attempt 2 of 4", record.Message);
        Assert.DoesNotContain(Recipient, record.Message);
        Assert.DoesNotContain(BodyText, record.Message);
    }

    [Fact]
    public async Task HandleAsync_WhenTheComposerThrows_CountsAFailureLogsAndRethrows()
    {
        var failure = new InvalidOperationException("The template could not be rendered.");
        MockComposer
            .Setup(x => x.ComposeAsync(Model, CancellationToken.None))
            .Throws(failure)
            .Verifiable();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Handler.HandleAsync(Job, Context, CancellationToken.None)
        );

        Assert.Same(failure, thrown);

        var measurement = Assert.Single(MessagesCollector.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(
            MailDeliveryMetrics.FailedResult,
            measurement.Tags[MailDeliveryMetrics.ResultAttribute]
        );

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(EventIds.MailDeliveryFailed, record.Id.Id);
        Assert.Same(failure, record.Exception);
        Assert.DoesNotContain(Recipient, record.Message);
    }

    [Fact]
    public async Task HandleAsync_WhenTheCallerCancels_PropagatesWithoutCountingOrLogging()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        await cancellationTokenSource.CancelAsync();

        var (message, stream) = CreateMessage();
        var cancellation = new OperationCanceledException(cancellationToken);
        MockComposer
            .Setup(x => x.ComposeAsync(Model, cancellationToken))
            .ReturnsAsync(message)
            .Verifiable();
        MockTransport
            .Setup(x => x.DeliverAsync(message, cancellationToken))
            .Throws(cancellation)
            .Verifiable();

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Handler.HandleAsync(Job, Context, cancellationToken)
        );

        Assert.Same(cancellation, thrown);
        Assert.False(stream.CanRead);
        Assert.Empty(MessagesCollector.GetMeasurementSnapshot());
        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task HandleAsync_WhenTheTransportFailsAfterTheCallerCancels_CountsAndLogsTheFailure()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        await cancellationTokenSource.CancelAsync();

        var (message, stream) = CreateMessage();
        var failure = new InvalidOperationException("The provider refused the message.");
        MockComposer
            .Setup(x => x.ComposeAsync(Model, cancellationToken))
            .ReturnsAsync(message)
            .Verifiable();
        MockTransport
            .Setup(x => x.DeliverAsync(message, cancellationToken))
            .Throws(failure)
            .Verifiable();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Handler.HandleAsync(Job, Context, cancellationToken)
        );

        Assert.Same(failure, thrown);
        Assert.False(stream.CanRead);

        var measurement = Assert.Single(MessagesCollector.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(
            MailDeliveryMetrics.FailedResult,
            measurement.Tags[MailDeliveryMetrics.ResultAttribute]
        );

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(EventIds.MailDeliveryFailed, record.Id.Id);
        Assert.Same(failure, record.Exception);
    }

    #endregion
}
