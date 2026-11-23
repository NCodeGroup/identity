// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Identity.Dispatch;
using Xunit;

namespace NCode.Identity.Mail.Tests;

public class DefaultMailSenderTests : BaseTests
{
    private sealed record CodeModel(string To, int Code);

    private sealed record UnknownModel(string Value);

    private Mock<IJobDispatcher> MockJobDispatcher { get; }

    private Mock<IMailComposer> MockComposer { get; }

    private DefaultMailSender Sender { get; }

    public DefaultMailSenderTests()
    {
        MockJobDispatcher = CreateStrictMock<IJobDispatcher>();

        MockComposer = CreateStrictMock<IMailComposer>();
        MockComposer.Setup(x => x.ModelType).Returns(typeof(CodeModel)).Verifiable();

        Sender = new DefaultMailSender(
            MockJobDispatcher.Object,
            new MailComposerRegistry([MockComposer.Object])
        );
    }

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_WithAComposedModel_DispatchesItWithShortRetries()
    {
        var model = new CodeModel("user@example.com", 123456);
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        MockJobDispatcher
            .Setup(x =>
                x.DispatchAsync(
                    It.Is<MailDeliveryJob>(job => ReferenceEquals(job.Model, model)),
                    DefaultMailSender.DeliveryOptions,
                    cancellationToken
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await Sender.SendAsync(model, cancellationToken);

        Assert.Equal(4, DefaultMailSender.DeliveryOptions.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), DefaultMailSender.DeliveryOptions.InitialBackoff);
        Assert.Equal(TimeSpan.FromMinutes(1), DefaultMailSender.DeliveryOptions.MaxBackoff);
    }

    [Fact]
    public async Task SendAsync_WithoutAComposer_ThrowsAndDispatchesNothing()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Sender.SendAsync(new UnknownModel("value"), CancellationToken.None)
        );

        Assert.Contains(typeof(UnknownModel).ToString(), exception.Message);
    }

    #endregion
}
