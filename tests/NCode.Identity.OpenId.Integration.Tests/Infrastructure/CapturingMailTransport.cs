// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using NCode.Identity.Mail;
using Xunit;

namespace NCode.Identity.OpenId.Integration.Tests.Infrastructure;

/// <summary>
/// Replaces the host's <see cref="IMailTransport"/> in a test: it keeps every message the host delivers, so the test
/// reads the code a flow mailed.
/// </summary>
// See ADR-1533.
public sealed partial class CapturingMailTransport : IMailTransport
{
    private static TimeSpan PollInterval { get; } = TimeSpan.FromMilliseconds(50);

    private ConcurrentQueue<MailMessage> Messages { get; } = new();

    /// <summary>
    /// Gets a snapshot of the messages delivered so far, in the order they arrived.
    /// </summary>
    public IReadOnlyList<MailMessage> Delivered => [.. Messages];

    /// <inheritdoc />
    public ValueTask DeliverAsync(MailMessage message, CancellationToken cancellationToken)
    {
        Messages.Enqueue(message);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Waits for a message addressed to <paramref name="address"/>, which the host delivers from a queue.
    /// </summary>
    /// <param name="address">The address the message is sent to.</param>
    /// <param name="timeout">The longest time to wait.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The first message addressed to <paramref name="address"/>.</returns>
    /// <exception cref="TimeoutException">No message arrived within <paramref name="timeout"/>.</exception>
    public async ValueTask<MailMessage> WaitForMessageToAsync(
        string address,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var message = Messages.FirstOrDefault(candidate =>
                string.Equals(candidate.To, address, StringComparison.OrdinalIgnoreCase)
            );
            if (message is not null)
                return message;

            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException($"No message was delivered to '{address}'.");

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Reads the six-digit code a message carries.
    /// </summary>
    /// <param name="message">The message a flow mailed.</param>
    /// <returns>The first run of six digits in the plain-text body.</returns>
    public static string ReadCode(MailMessage message)
    {
        var match = CodePattern().Match(message.TextBody);
        Assert.True(match.Success, message.TextBody);
        return match.Value;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();
}
