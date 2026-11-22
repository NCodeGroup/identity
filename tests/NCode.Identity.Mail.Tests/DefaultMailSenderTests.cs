// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Identity.Dispatch;
using Xunit;

namespace NCode.Identity.Mail.Tests;

public class DefaultMailSenderTests : BaseTests
{
    private Mock<IJobDispatcher> MockJobDispatcher { get; }

    private DefaultMailSender Sender { get; }

    public DefaultMailSenderTests()
    {
        MockJobDispatcher = CreateStrictMock<IJobDispatcher>();
        Sender = new DefaultMailSender(MockJobDispatcher.Object);
    }

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_Always_DispatchesTheMessageWithShortRetries()
    {
        var message = new MailMessage
        {
            To = "user@example.com",
            Subject = "Subject",
            TextBody = "Body",
        };
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        MockJobDispatcher
            .Setup(x =>
                x.DispatchAsync(
                    It.Is<MailDeliveryJob>(job => ReferenceEquals(job.Message, message)),
                    DefaultMailSender.DeliveryOptions,
                    cancellationToken
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await Sender.SendAsync(message, cancellationToken);

        Assert.Equal(4, DefaultMailSender.DeliveryOptions.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), DefaultMailSender.DeliveryOptions.InitialBackoff);
        Assert.Equal(TimeSpan.FromMinutes(1), DefaultMailSender.DeliveryOptions.MaxBackoff);
    }

    #endregion
}
