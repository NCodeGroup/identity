// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using Microsoft.AspNetCore.DataProtection;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

public sealed class DefaultPageCursorProtectorTests
{
    private const string Collection = "ClientEntity";

    private static byte[] CreatePosition()
    {
        var position = new byte[16];
        for (var i = 0; i < position.Length; i++)
            position[i] = (byte)(i + 1);
        return position;
    }

    private static DefaultPageCursorProtector CreateProtector()
    {
        var provider = new EphemeralDataProtectionProvider();
        return new DefaultPageCursorProtector(provider);
    }

    #region Protect and TryUnprotect Tests

    [Fact]
    public void Protect_ThenTryUnprotect_ReturnsThePosition()
    {
        var protector = CreateProtector();
        var position = CreatePosition();
        var destination = new byte[32];

        var cursor = protector.Protect(Collection, position);
        var result = protector.TryUnprotect(Collection, cursor, destination, out var bytesWritten);
        var written = destination[..bytesWritten];

        Assert.True(result);
        Assert.Equal(position.Length, bytesWritten);
        Assert.Equal(position, written);
    }

    [Fact]
    public void TryUnprotect_DestinationTooSmall_ReturnsFalse()
    {
        var protector = CreateProtector();
        var position = CreatePosition();
        var cursor = protector.Protect(Collection, position);
        var destination = new byte[15];

        var result = protector.TryUnprotect(Collection, cursor, destination, out var bytesWritten);

        Assert.False(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryUnprotect_TamperedCursor_ReturnsFalse()
    {
        var protector = CreateProtector();
        var position = CreatePosition();
        var cursor = protector.Protect(Collection, position);
        var replacement = cursor[20] == 'A' ? 'B' : 'A';
        var prefix = cursor.AsSpan(0, 20);
        var suffix = cursor.AsSpan(21);
        var tampered = string.Concat(prefix, [replacement], suffix);
        var destination = new byte[32];

        var result = protector.TryUnprotect(
            Collection,
            tampered,
            destination,
            out var bytesWritten
        );

        Assert.False(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryUnprotect_OtherCollection_ReturnsFalse()
    {
        var protector = CreateProtector();
        var position = CreatePosition();
        var cursor = protector.Protect(Collection, position);
        var destination = new byte[32];

        var result = protector.TryUnprotect(
            "ConsentEntity",
            cursor,
            destination,
            out var bytesWritten
        );

        Assert.False(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryUnprotect_OtherKeyRing_ReturnsFalse()
    {
        var protector = CreateProtector();
        var otherProtector = CreateProtector();
        var position = CreatePosition();
        var cursor = protector.Protect(Collection, position);
        var destination = new byte[32];

        var result = otherProtector.TryUnprotect(
            Collection,
            cursor,
            destination,
            out var bytesWritten
        );

        Assert.False(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryUnprotect_MalformedCursor_ReturnsFalse()
    {
        var protector = CreateProtector();
        var destination = new byte[32];

        var result = protector.TryUnprotect(Collection, "!!", destination, out var bytesWritten);

        Assert.False(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void Protect_Position_DoesNotAppearInTheCursor()
    {
        var protector = CreateProtector();
        var position = CreatePosition();

        var cursor = protector.Protect(Collection, position);
        var bytes = Base64Url.DecodeFromChars(cursor);
        var index = bytes.AsSpan().IndexOf(position.AsSpan());

        Assert.True(index < 0);
    }

    #endregion
}
