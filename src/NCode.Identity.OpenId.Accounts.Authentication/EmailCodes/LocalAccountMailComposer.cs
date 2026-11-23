// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net.Mime;
using System.Text;
using NCode.Buffers;
using NCode.Identity.Mail;
using NCode.Identity.OpenId.Authentication.Interactive;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Composes the plain-text mail that carries a one-time code, in the wording of an <see cref="InteractiveLocale"/>,
/// from a <see cref="LocalAccountCodeMail"/> at each delivery attempt.
/// </summary>
// See ADR-1533.
internal sealed class LocalAccountMailComposer : MailComposer<LocalAccountCodeMail>
{
    private const int InitialBodyChars = 256;

    private const string BodyCharset = "utf-8";

    /// <inheritdoc />
    public override ValueTask<MailMessage> ComposeAsync(
        LocalAccountCodeMail model,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(model);

        var to = new MailAddress(model.To);
        var stream = BufferFactory.CreatePooledStream(isSensitive: true);
        try
        {
            WriteBody(stream, model);
            stream.Position = 0;

            var message = new MailMessage
            {
                To = [to],
                FromDisplayName = model.FromDisplayName,
                Subject = model.Subject,
                TextBody = new StreamMailContent(stream, MediaTypeNames.Text.Plain, BodyCharset),
            };
            return ValueTask.FromResult(message);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Writes the body of a mail as UTF-8 into a stream. The text is formatted into a sensitive buffer that doubles
    /// until the body fits.
    /// </summary>
    /// <param name="destination">The stream that receives the bytes of the body.</param>
    /// <param name="model">The model that holds the format, the code and the lifetime.</param>
    private static void WriteBody(Stream destination, LocalAccountCodeMail model)
    {
        var culture = GetCulture(model.LanguageTag);
        var format = CompositeFormat.Parse(model.BodyFormat);
        var code = new EmailCodeText(model.Code);

        var size = InitialBodyChars;
        while (true)
        {
            using var charsOwner = BufferFactory.Rent<char>(
                size,
                isSensitive: true,
                out Span<char> chars
            );

            if (chars.TryWrite(culture, format, out var written, code, model.LifetimeMinutes))
            {
                using var bytesOwner = SecureEncoding.UTF8.RentBytes(
                    chars[..written],
                    isSensitive: true,
                    out Span<byte> bytes
                );
                destination.Write(bytes);
                return;
            }

            size *= 2;
        }
    }

    /// <summary>
    /// Gets the culture a mail is worded in. A host may register a locale whose tag the platform has no culture data
    /// for, which words the mail all the same.
    /// </summary>
    /// <param name="languageTag">The language tag of the locale.</param>
    /// <returns>The culture of the tag, or the invariant culture when the platform has none for it.</returns>
    private static CultureInfo GetCulture(string languageTag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageTag);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
