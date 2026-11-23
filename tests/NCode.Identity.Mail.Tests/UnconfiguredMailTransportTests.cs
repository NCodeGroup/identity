// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.Mail.Tests;

public class UnconfiguredMailTransportTests : BaseTests
{
    #region DeliverAsync Tests

    [Fact]
    public async Task DeliverAsync_Always_ThrowsNamingTheMissingTransport()
    {
        var transport = new UnconfiguredMailTransport();
        using var message = new MailMessage
        {
            To = [new MailAddress("user@example.com")],
            Subject = "Subject",
            TextBody = new StreamMailContent(
                new MemoryStream("Body"u8.ToArray()),
                "text/plain",
                "utf-8"
            ),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.DeliverAsync(message, CancellationToken.None)
        );

        Assert.Contains("No mail transport is registered", exception.Message);
        Assert.Contains("AddSmtpMailTransport", exception.Message);
    }

    #endregion
}
