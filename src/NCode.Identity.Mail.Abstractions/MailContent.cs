// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Buffers;

namespace NCode.Identity.Mail;

/// <summary>
/// Represents the body, an attachment or a linked resource of a mail message, as a media type and a stream of bytes.
/// Like <see cref="HttpContent"/>, the content owns what it reads from, and the message that holds the content disposes
/// it.
/// </summary>
/// <remarks>
/// Reusable content, such as the content of a seekable stream, is read as often as a transport needs. Other content is
/// read once, unless <see cref="LoadIntoBufferAsync"/> copies it first into a sensitive pooled buffer that the content
/// owns, which every later read rewinds.
/// </remarks>
// See ADR-1532.
[PublicAPI]
public abstract class MailContent : IDisposable, IAsyncDisposable
{
    private const int CopyChunkBytes = 16384;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailContent"/> class.
    /// </summary>
    /// <param name="mediaType">The media type of the content in the form <c>type/subtype</c>.</param>
    /// <param name="charset">The character set of text content, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="mediaType"/> is not in the form <c>type/subtype</c> or holds a control character or
    /// a semicolon.
    /// </exception>
    protected MailContent(string mediaType, string? charset)
    {
        ArgumentException.ThrowIfNullOrEmpty(mediaType);

        var slash = mediaType.IndexOf('/');
        var isTypeAndSubtype =
            slash > 0 && slash < mediaType.Length - 1 && mediaType.IndexOf('/', slash + 1) < 0;
        var hasInvalidCharacter =
            mediaType.AsSpan().ContainsAnyInRange('\u0000', '\u001F')
            || mediaType.AsSpan().ContainsAny('\u007F', ';');

        if (!isTypeAndSubtype || hasInvalidCharacter)
            throw new ArgumentException(
                "The media type must be in the form 'type/subtype' with no control character or semicolon.",
                nameof(mediaType)
            );

        MediaType = mediaType;
        Charset = charset;
    }

    /// <summary>
    /// Gets the media type of the content in the form <c>type/subtype</c>.
    /// </summary>
    public string MediaType { get; }

    /// <summary>
    /// Gets the character set of text content, or <see langword="null"/> for none.
    /// </summary>
    public string? Charset { get; }

    /// <summary>
    /// Gets the transfer encoding the content is written with, one of <see cref="MailTransferEncodings"/>, or
    /// <see langword="null"/> to let the transport pick one for the media type.
    /// </summary>
    public string? TransferEncoding { get; init; }

    /// <summary>
    /// Gets a value indicating whether the content can be read more than once.
    /// </summary>
    public abstract bool IsReusable { get; }

    private PooledMemoryStream? LoadedBuffer { get; set; }

    private bool HasBeenRead { get; set; }

    private bool IsDisposed { get; set; }

    /// <summary>
    /// Opens the stream of the content, which the content owns and disposes.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that completes with the stream, positioned at the start of the content.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the content is not reusable, is not loaded into a buffer and has been read already.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown when the content is disposed.</exception>
    public async ValueTask<Stream> ReadAsStreamAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (LoadedBuffer is { } buffer)
        {
            buffer.Position = 0;
            return buffer;
        }

        if (!IsReusable)
        {
            if (HasBeenRead)
                throw new InvalidOperationException(
                    "The content is not reusable and has been read already."
                );

            HasBeenRead = true;
        }

        return await CreateReadStreamAsync(cancellationToken);
    }

    /// <summary>
    /// Copies content that is not reusable once into a sensitive pooled buffer that the content owns, so that every
    /// later read of the content rewinds the buffer. It does nothing for reusable content and for content that is
    /// loaded already.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that completes once the content is buffered.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the content is not reusable and has been read already.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown when the content is disposed.</exception>
    public async ValueTask LoadIntoBufferAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (IsReusable || LoadedBuffer is not null)
            return;

        var source = await ReadAsStreamAsync(cancellationToken);
        var buffer = BufferFactory.CreatePooledStream(isSensitive: true);
        try
        {
            // The chunk is a sensitive rental, which Stream.CopyToAsync would draw from the shared pool instead.
            using var lease = BufferFactory.Rent(
                CopyChunkBytes,
                isSensitive: true,
                out Memory<byte> chunk
            );

            while (true)
            {
                var read = await source.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                    break;

                buffer.Write(chunk.Span[..read]);
            }
        }
        catch
        {
            await buffer.DisposeAsync();
            throw;
        }

        LoadedBuffer = buffer;
    }

    /// <summary>
    /// Opens the stream of the content for its first read, or for each read when <see cref="IsReusable"/> is
    /// <see langword="true"/>.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that completes with the stream, positioned at the start of the content.
    /// </returns>
    protected abstract ValueTask<Stream> CreateReadStreamAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the resources the content owns.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when called from <see cref="Dispose()"/>; otherwise, <see langword="false"/>.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        LoadedBuffer?.Dispose();
        LoadedBuffer = null;
    }

    /// <summary>
    /// Releases the resources the content owns, asynchronously.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that completes once the resources are released.</returns>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (LoadedBuffer is { } buffer)
            await buffer.DisposeAsync();

        LoadedBuffer = null;
    }
}
