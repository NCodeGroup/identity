// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Identity.Persistence.Stores;

public class PageCursorProtectorExtensionsTests
{
    private sealed class FakePageCursorProtector : IPageCursorProtector
    {
        public string ProtectedCursor { get; init; } = "cursor";

        public byte[]? UnprotectedPosition { get; init; }

        public bool UnprotectResult { get; init; }

        public int ProtectCount { get; private set; }

        public int TryUnprotectCount { get; private set; }

        public string? ProtectedCollection { get; private set; }

        public byte[]? ProtectedPosition { get; private set; }

        public string Protect(string collection, ReadOnlySpan<byte> position)
        {
            ProtectCount++;
            ProtectedCollection = collection;
            ProtectedPosition = position.ToArray();
            return ProtectedCursor;
        }

        public bool TryUnprotect(
            string collection,
            string cursor,
            Span<byte> destination,
            out int bytesWritten
        )
        {
            TryUnprotectCount++;
            bytesWritten = 0;

            if (!UnprotectResult || UnprotectedPosition is null)
                return false;

            if (UnprotectedPosition.Length > destination.Length)
                return false;

            UnprotectedPosition.CopyTo(destination);
            bytesWritten = UnprotectedPosition.Length;
            return true;
        }
    }

    #region ProtectKey Tests

    [Fact]
    public void ProtectKey_Key_ProtectsItsBigEndianBytes()
    {
        var protector = new FakePageCursorProtector { ProtectedCursor = "opaque" };
        var key = Guid.CreateVersion7();

        var cursor = protector.ProtectKey("collection", key);

        Assert.Equal("opaque", cursor);
        Assert.Equal(1, protector.ProtectCount);
        Assert.Equal("collection", protector.ProtectedCollection);
        Assert.Equal(key.ToByteArray(bigEndian: true), protector.ProtectedPosition);
    }

    #endregion

    #region UnprotectKey Tests

    [Fact]
    public void UnprotectKey_ProtectedKey_ReturnsTheKey()
    {
        var key = Guid.CreateVersion7();
        var protector = new FakePageCursorProtector
        {
            UnprotectResult = true,
            UnprotectedPosition = key.ToByteArray(bigEndian: true),
        };

        var result = protector.UnprotectKey("collection", "cursor");

        Assert.Equal(key, result);
        Assert.Equal(1, protector.TryUnprotectCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UnprotectKey_NullOrEmptyCursor_ReturnsNull(string? cursor)
    {
        var protector = new FakePageCursorProtector
        {
            UnprotectResult = true,
            UnprotectedPosition = Guid.CreateVersion7().ToByteArray(bigEndian: true),
        };

        var result = protector.UnprotectKey("collection", cursor);

        Assert.Null(result);
        Assert.Equal(0, protector.TryUnprotectCount);
    }

    [Fact]
    public void UnprotectKey_UnprotectFails_ReturnsNull()
    {
        var protector = new FakePageCursorProtector { UnprotectResult = false };

        var result = protector.UnprotectKey("collection", "cursor");

        Assert.Null(result);
        Assert.Equal(1, protector.TryUnprotectCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(24)]
    public void UnprotectKey_PositionOfAnotherLength_ReturnsNull(int length)
    {
        var protector = new FakePageCursorProtector
        {
            UnprotectResult = true,
            UnprotectedPosition = new byte[length],
        };

        var result = protector.UnprotectKey("collection", "cursor");

        Assert.Null(result);
        Assert.Equal(1, protector.TryUnprotectCount);
    }

    #endregion
}
