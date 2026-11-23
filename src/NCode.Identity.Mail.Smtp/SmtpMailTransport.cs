// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using NCode.Identity.Mail.Smtp.Facades;
using NCode.Identity.Mail.Smtp.Logging;
using NCode.Identity.Mail.Smtp.Options;

namespace NCode.Identity.Mail.Smtp;

/// <summary>
/// Provides the <see cref="IMailTransport"/> that submits each message to the configured SMTP server. A server that
/// refuses the message makes the delivery throw a <see cref="SmtpDeliveryException"/> that carries the numeric status
/// and not the server's reply text, which can echo the recipient's address; a server that cannot be reached makes the
/// delivery throw the connection's own exception. Either way the delivery is retried.
/// </summary>
// See ADR-1532.
internal sealed class SmtpMailTransport(
    ISmtpClientFactory clientFactory,
    IOptions<SmtpMailTransportOptions> optionsAccessor,
    ILogger<SmtpMailTransport> logger
) : IMailTransport
{
    private ISmtpClientFactory ClientFactory { get; } = clientFactory;

    private ILogger<SmtpMailTransport> Logger { get; } = logger;

    private SmtpMailTransportOptions Options => optionsAccessor.Value;

    private static SecureSocketOptions ToSocketOptions(SmtpTls tls) =>
        tls switch
        {
            SmtpTls.StartTls => SecureSocketOptions.StartTls,
            SmtpTls.ImplicitTls => SecureSocketOptions.SslOnConnect,
            SmtpTls.None => SecureSocketOptions.None,
            _ => throw new ArgumentOutOfRangeException(
                nameof(tls),
                tls,
                "The TLS mode is not a defined value."
            ),
        };

    private MimeMessage CreateMimeMessage(MailMessage message)
    {
        var options = Options;

        var mimeMessage = new MimeMessage();

        var fromDisplayName = message.FromDisplayName ?? options.FromName ?? string.Empty;
        var fromAddress = new MailboxAddress(fromDisplayName, options.From);
        mimeMessage.From.Add(fromAddress);

        var toAddress = MailboxAddress.Parse(message.To);
        mimeMessage.To.Add(toAddress);
        mimeMessage.Subject = message.Subject;
        mimeMessage.Headers.Add("Auto-Submitted", "auto-generated");

        var textPart = new TextPart("plain") { Text = message.TextBody };
        if (message.HtmlBody is null)
        {
            mimeMessage.Body = textPart;
        }
        else
        {
            var htmlPart = new TextPart("html") { Text = message.HtmlBody };
            var alternative = new MultipartAlternative { textPart, htmlPart };
            mimeMessage.Body = alternative;
        }

        return mimeMessage;
    }

    /// <inheritdoc />
    public async ValueTask DeliverAsync(MailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var options = Options;
        var mimeMessage = CreateMimeMessage(message);

        using var client = ClientFactory.Create();
        client.Timeout = (int)options.Timeout.TotalMilliseconds;

        var socketOptions = ToSocketOptions(options.Tls);
        try
        {
            await client.ConnectAsync(options.Host, options.Port, socketOptions, cancellationToken);

            if (!string.IsNullOrEmpty(options.UserName))
            {
                var password = options.Password ?? string.Empty;
                await client.AuthenticateAsync(options.UserName, password, cancellationToken);
            }

            await client.SendAsync(mimeMessage, cancellationToken);
        }
        catch (SmtpCommandException exception)
        {
            // The reply text of the server can echo the recipient's address, so only its status leaves this method.
            throw new SmtpDeliveryException(exception.StatusCode);
        }

        try
        {
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception exception)
        {
            // The server accepted the message, so a failed close neither fails nor repeats the delivery, and only the
            // exception's type is logged because a reply can echo the recipient (See ADR-1532).
            var exceptionType = exception.GetType().Name;
            Logger.SmtpQuitFailed(exceptionType);
        }
    }
}
