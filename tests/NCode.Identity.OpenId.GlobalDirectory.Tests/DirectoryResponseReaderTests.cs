// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Tests;

public class DirectoryResponseReaderTests
{
    private const string Endpoint = "The token endpoint";

    [Fact]
    public async Task ReadAsync_WhenTheBodyIsOneByteOverTheLimit_ThrowsDirectoryUnavailableException()
    {
        var body = new byte[DirectoryClientDefaults.MaxTokenResponseBytes + 1];
        using var content = new ByteArrayContent(body);

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await DirectoryResponseReader.ReadAsync(
                content,
                DirectoryClientDefaults.MaxTokenResponseBytes,
                Endpoint,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ReadAsync_WhenAnUnsizedBodyIsOneByteOverTheLimit_ThrowsDirectoryUnavailableException()
    {
        var body = new byte[DirectoryClientDefaults.MaxTokenResponseBytes + 1];
        using var content = new StreamContent(new UnsizedStream(body));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await DirectoryResponseReader.ReadAsync(
                content,
                DirectoryClientDefaults.MaxTokenResponseBytes,
                Endpoint,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ReadAsync_WhenTheBodyIsAtTheLimit_ReturnsTheBody()
    {
        var body = new byte[DirectoryClientDefaults.MaxTokenResponseBytes];
        Array.Fill(body, (byte)7);
        using var content = new ByteArrayContent(body);

        var result = await DirectoryResponseReader.ReadAsync(
            content,
            DirectoryClientDefaults.MaxTokenResponseBytes,
            Endpoint,
            CancellationToken.None
        );

        Assert.Equal(body, result);
    }

    [Fact]
    public async Task ReadAsync_WhenAnUnsizedBodyIsAtTheLimit_ReturnsTheBody()
    {
        var body = new byte[DirectoryClientDefaults.MaxTokenResponseBytes];
        Array.Fill(body, (byte)7);
        using var content = new StreamContent(new UnsizedStream(body));

        var result = await DirectoryResponseReader.ReadAsync(
            content,
            DirectoryClientDefaults.MaxTokenResponseBytes,
            Endpoint,
            CancellationToken.None
        );

        Assert.Equal(body, result);
    }

    // A stream that cannot seek, so its content has no length and the reader counts the bytes it reads.
    private sealed class UnsizedStream(byte[] body) : Stream
    {
        private MemoryStream Inner { get; } = new(body);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Inner.Read(buffer, offset, count);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
