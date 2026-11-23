// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.Mail.AmazonSes.Facades;
using NCode.Identity.Mail.AmazonSes.Options;
using NCode.Identity.Mail.Mime;

namespace NCode.Identity.Mail.AmazonSes;

/// <summary>
/// Provides the <see cref="IMailTransport"/> that sends each message through the Amazon SES v2 API as a raw MIME
/// message, with every recipient, blind copies included, in the envelope. A refusal by Amazon SES makes the delivery
/// throw an <see cref="AmazonSesDeliveryException"/> that carries the error code and the HTTP status and not the
/// service's message text, which can echo the recipient's address; a call that fails otherwise makes the delivery throw
/// the call's own exception. Either way the delivery is retried.
/// </summary>
// See ADR-1528.
internal sealed class AmazonSesMailTransport(
    IAmazonSesClientFactory clientFactory,
    IOptions<AmazonSesMailTransportOptions> optionsAccessor
) : IMailTransport
{
    private IAmazonSesClientFactory ClientFactory { get; } = clientFactory;

    private AmazonSesMailTransportOptions Options => optionsAccessor.Value;

    /// <summary>
    /// Maps an empty option value to <see langword="null"/>, so the request leaves out a field that the options leave
    /// blank.
    /// </summary>
    /// <param name="value">The option value, or <see langword="null"/> for none.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is <see langword="null"/> or empty; otherwise
    /// <paramref name="value"/>.
    /// </returns>
    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Lists the addresses of the mailboxes, in order and without their display names, as the envelope of the request
    /// holds them.
    /// </summary>
    /// <param name="addresses">The mailboxes to list.</param>
    /// <returns>The address of each mailbox.</returns>
    private static List<string> ToAddresses(List<MailAddress> addresses) =>
        addresses.Select(address => address.Address).ToList();

    /// <inheritdoc />
    public async ValueTask DeliverAsync(MailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var options = Options;
        var defaultFrom = new MailAddress(options.From, options.FromName);
        using var mimeMessage = await MimeMailMessages.CreateAsync(
            message,
            defaultFrom,
            cancellationToken
        );
        var formatOptions = MimeMailMessages.CreateFormatOptions();

        await using var raw = BufferFactory.CreatePooledStream(isSensitive: true);
        await mimeMessage.WriteToAsync(formatOptions, raw, cancellationToken);

        if (!MemoryMarshal.TryGetArray(raw.WrittenMemory, out var segment) || segment.Array is null)
            throw new InvalidOperationException("The raw message is not backed by an array.");

        // The SDK takes the raw message only as a MemoryStream, which reads the rented array in place.
        using var data = new MemoryStream(
            segment.Array,
            segment.Offset,
            segment.Count,
            writable: false
        );

        var from = message.From?.Address ?? options.From;
        var request = new SendEmailRequest
        {
            FromEmailAddress = from,
            FromEmailAddressIdentityArn = NullIfEmpty(options.FromIdentityArn),
            ConfigurationSetName = NullIfEmpty(options.ConfigurationSetName),
            Destination = new Destination
            {
                ToAddresses = ToAddresses(message.To),
                CcAddresses = ToAddresses(message.Cc),
                BccAddresses = ToAddresses(message.Bcc),
            },
            Content = new EmailContent { Raw = new RawMessage { Data = data } },
        };

        if (options.Tags.Count > 0)
        {
            request.EmailTags = [];
            foreach (var (name, value) in options.Tags)
            {
                var tag = new MessageTag { Name = name, Value = value };
                request.EmailTags.Add(tag);
            }
        }

        var client = ClientFactory.GetClient();
        try
        {
            await client.SendEmailAsync(request, cancellationToken);
        }
        catch (AmazonSimpleEmailServiceV2Exception exception)
        {
            // The message text of Amazon SES can echo the recipient's address, so only the code and the status leave
            // this method.
            var statusCode = (int)exception.StatusCode;
            throw new AmazonSesDeliveryException(exception.ErrorCode, statusCode);
        }
    }
}
