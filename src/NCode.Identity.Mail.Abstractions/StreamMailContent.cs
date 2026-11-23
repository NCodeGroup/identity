// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Represents mail content that is read from a stream, which the content owns and disposes with itself.
/// </summary>
/// <remarks>
/// The content is the stream from its start. A seekable stream makes the content reusable, and each read rewinds it. A
/// stream that does not seek is read once, unless <see cref="MailContent.LoadIntoBufferAsync"/> copies it first.
/// </remarks>
/// <param name="stream">The stream that holds the content from its start, which the content owns.</param>
/// <param name="mediaType">The media type of the content in the form <c>type/subtype</c>.</param>
/// <param name="charset">The character set of text content, or <see langword="null"/> for none.</param>
// See ADR-1532.
[PublicAPI]
public sealed class StreamMailContent(Stream stream, string mediaType, string? charset = null)
    : MailContent(mediaType, charset)
{
    private Stream Stream { get; } = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <inheritdoc />
    public override bool IsReusable => Stream.CanSeek;

    /// <inheritdoc />
    protected override ValueTask<Stream> CreateReadStreamAsync(CancellationToken cancellationToken)
    {
        if (Stream.CanSeek)
            Stream.Position = 0;

        return ValueTask.FromResult(Stream);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Stream.Dispose();

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        await Stream.DisposeAsync();
        await base.DisposeAsyncCore();
    }
}
