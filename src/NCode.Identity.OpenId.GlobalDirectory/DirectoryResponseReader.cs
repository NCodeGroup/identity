// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Buffers;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Reads the body of a response from the directory, up to a limit that the caller names.
/// </summary>
internal static class DirectoryResponseReader
{
    private const int ReadChunkBytes = 4096;

    /// <summary>
    /// Reads the body of a response, up to <paramref name="maxBytes"/>.
    /// </summary>
    /// <param name="content">The <see cref="HttpContent"/> of the response.</param>
    /// <param name="maxBytes">The most bytes that the body may carry.</param>
    /// <param name="endpoint">The endpoint that answered, which names it in an error.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The bytes of the body.</returns>
    /// <exception cref="DirectoryUnavailableException">The body is larger than <paramref name="maxBytes"/>.</exception>
    internal static async ValueTask<byte[]> ReadAsync(
        HttpContent content,
        int maxBytes,
        string endpoint,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Headers.ContentLength > maxBytes)
            throw new DirectoryUnavailableException(
                $"{endpoint} answered with a body larger than {maxBytes} bytes."
            );

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = BufferFactory.CreatePooledStream(isSensitive: false);
        var chunk = new byte[ReadChunkBytes];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                return buffer.WrittenSpan.ToArray();

            if (buffer.Length + read > maxBytes)
                throw new DirectoryUnavailableException(
                    $"{endpoint} answered with a body larger than {maxBytes} bytes."
                );

            buffer.Write(chunk, 0, read);
        }
    }
}
