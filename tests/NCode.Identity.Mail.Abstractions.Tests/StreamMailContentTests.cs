// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using Xunit;

namespace NCode.Identity.Mail.Abstractions.Tests;

public class StreamMailContentTests : BaseTests
{
    private static byte[] Bytes { get; } = [1, 2, 3, 4, 5, 6, 7, 8];

    private static MemoryStream CreateSeekableStream() => new(Bytes);

    private static DeflateStream CreateNonSeekableStream()
    {
        var compressed = new MemoryStream();
        using (
            var deflate = new DeflateStream(compressed, CompressionMode.Compress, leaveOpen: true)
        )
            deflate.Write(Bytes);

        compressed.Position = 0;
        return new DeflateStream(compressed, CompressionMode.Decompress);
    }

    private static async ValueTask<byte[]> ReadAllAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        return copy.ToArray();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithAMediaTypeAndACharset_SetsBoth()
    {
        using var content = new StreamMailContent(CreateSeekableStream(), "text/plain", "utf-8");

        Assert.Equal("text/plain", content.MediaType);
        Assert.Equal("utf-8", content.Charset);
        Assert.Null(content.TransferEncoding);
    }

    [Theory]
    [InlineData("")]
    [InlineData("text")]
    [InlineData("/plain")]
    [InlineData("text/")]
    [InlineData("text/plain/extra")]
    [InlineData("text/pla;in")]
    [InlineData("text/plain; charset=utf-8")]
    [InlineData("text/pl\r\nain")]
    [InlineData("text/pla\u007Fin")]
    public void Constructor_WithAMalformedMediaType_Throws(string mediaType)
    {
        using var stream = CreateSeekableStream();

        Assert.Throws<ArgumentException>(() => new StreamMailContent(stream, mediaType));
    }

    #endregion

    #region IsReusable Tests

    [Fact]
    public void IsReusable_WithASeekableStream_ReturnsTrue()
    {
        using var content = new StreamMailContent(
            CreateSeekableStream(),
            "application/octet-stream"
        );

        Assert.True(content.IsReusable);
    }

    [Fact]
    public void IsReusable_WithANonSeekableStream_ReturnsFalse()
    {
        using var content = new StreamMailContent(
            CreateNonSeekableStream(),
            "application/octet-stream"
        );

        Assert.False(content.IsReusable);
    }

    #endregion

    #region ReadAsStreamAsync Tests

    [Fact]
    public async Task ReadAsStreamAsync_WithASeekableStream_RewindsOnEachRead()
    {
        await using var content = new StreamMailContent(
            CreateSeekableStream(),
            "application/octet-stream"
        );

        var first = await content.ReadAsStreamAsync(CancellationToken.None);
        var firstBytes = await ReadAllAsync(first, CancellationToken.None);
        var second = await content.ReadAsStreamAsync(CancellationToken.None);
        var secondBytes = await ReadAllAsync(second, CancellationToken.None);

        Assert.Equal(Bytes, firstBytes);
        Assert.Equal(Bytes, secondBytes);
    }

    [Fact]
    public async Task ReadAsStreamAsync_WithANonSeekableStreamTwice_Throws()
    {
        await using var content = new StreamMailContent(
            CreateNonSeekableStream(),
            "application/octet-stream"
        );

        var stream = await content.ReadAsStreamAsync(CancellationToken.None);
        var bytes = await ReadAllAsync(stream, CancellationToken.None);

        Assert.Equal(Bytes, bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await content.ReadAsStreamAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task ReadAsStreamAsync_AfterDisposal_Throws()
    {
        var content = new StreamMailContent(CreateSeekableStream(), "application/octet-stream");
        content.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await content.ReadAsStreamAsync(CancellationToken.None)
        );
    }

    #endregion

    #region LoadIntoBufferAsync Tests

    [Fact]
    public async Task LoadIntoBufferAsync_WithANonSeekableStream_ReadsTheSameBytesTwice()
    {
        await using var content = new StreamMailContent(
            CreateNonSeekableStream(),
            "application/octet-stream"
        );

        await content.LoadIntoBufferAsync(CancellationToken.None);
        var first = await content.ReadAsStreamAsync(CancellationToken.None);
        var firstBytes = await ReadAllAsync(first, CancellationToken.None);
        var second = await content.ReadAsStreamAsync(CancellationToken.None);
        var secondBytes = await ReadAllAsync(second, CancellationToken.None);

        Assert.Equal(Bytes, firstBytes);
        Assert.Equal(Bytes, secondBytes);
    }

    [Fact]
    public async Task LoadIntoBufferAsync_Twice_BuffersOnce()
    {
        await using var content = new StreamMailContent(
            CreateNonSeekableStream(),
            "application/octet-stream"
        );

        await content.LoadIntoBufferAsync(CancellationToken.None);
        var first = await content.ReadAsStreamAsync(CancellationToken.None);
        await content.LoadIntoBufferAsync(CancellationToken.None);
        var second = await content.ReadAsStreamAsync(CancellationToken.None);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task LoadIntoBufferAsync_WithASeekableStream_LeavesTheStreamAsTheSource()
    {
        using var stream = CreateSeekableStream();
        await using var content = new StreamMailContent(stream, "application/octet-stream");

        await content.LoadIntoBufferAsync(CancellationToken.None);
        var read = await content.ReadAsStreamAsync(CancellationToken.None);

        Assert.Same(stream, read);
    }

    [Fact]
    public async Task LoadIntoBufferAsync_WithANonSeekableStreamThatWasRead_Throws()
    {
        await using var content = new StreamMailContent(
            CreateNonSeekableStream(),
            "application/octet-stream"
        );
        await content.ReadAsStreamAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await content.LoadIntoBufferAsync(CancellationToken.None)
        );
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_Always_DisposesTheStream()
    {
        var stream = CreateSeekableStream();
        var content = new StreamMailContent(stream, "application/octet-stream");

        content.Dispose();
        content.Dispose();

        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task DisposeAsync_Always_DisposesTheStream()
    {
        var stream = CreateSeekableStream();
        var content = new StreamMailContent(stream, "application/octet-stream");

        await content.DisposeAsync();
        await content.DisposeAsync();

        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task DisposeAsync_AfterLoadingIntoABuffer_DisposesTheBuffer()
    {
        var content = new StreamMailContent(CreateNonSeekableStream(), "application/octet-stream");
        await content.LoadIntoBufferAsync(CancellationToken.None);
        var buffer = await content.ReadAsStreamAsync(CancellationToken.None);

        await content.DisposeAsync();

        Assert.False(buffer.CanRead);
    }

    #endregion
}
