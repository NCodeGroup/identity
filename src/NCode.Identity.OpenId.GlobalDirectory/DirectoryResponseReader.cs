// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Reads the body of a response from the directory, up to a limit that the caller names.
/// </summary>
internal static class DirectoryResponseReader
{
    /// <summary>
    /// Reads the body of a response into <paramref name="destination"/>, up to <paramref name="maxBytes"/>.
    /// </summary>
    /// <param name="content">The <see cref="HttpContent"/> of the response.</param>
    /// <param name="destination">The writer that receives the body's bytes.</param>
    /// <param name="maxBytes">The most bytes that the body may carry.</param>
    /// <param name="endpoint">The endpoint that answered, which names it in an error.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A task that completes when the whole body is in <paramref name="destination"/>.</returns>
    /// <exception cref="DirectoryUnavailableException">The body is larger than <paramref name="maxBytes"/>.</exception>
    internal static async ValueTask ReadAsync(
        HttpContent content,
        IBufferWriter<byte> destination,
        int maxBytes,
        string endpoint,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);

        if (content.Headers.ContentLength > maxBytes)
            throw new DirectoryUnavailableException(
                $"{endpoint} answered with a body larger than {maxBytes} bytes."
            );

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var total = 0;
        while (true)
        {
            var memory = destination.GetMemory(sizeHint: 0);
            var read = await stream.ReadAsync(memory, cancellationToken);
            if (read == 0)
                return;

            // The cap is checked before the chunk is committed, so overflow bytes are never exposed to the caller.
            if (total + read > maxBytes)
                throw new DirectoryUnavailableException(
                    $"{endpoint} answered with a body larger than {maxBytes} bytes."
                );

            total += read;
            destination.Advance(read);
        }
    }
}
