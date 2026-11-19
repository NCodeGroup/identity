// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using Microsoft.AspNetCore.DataProtection;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

public sealed class DefaultPageCursorProtectorTests
{
    private const string Collection = "ClientEntity";

    private static byte[] CreatePosition() =>
        Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();

    private static DefaultPageCursorProtector CreateProtector() =>
        new(new EphemeralDataProtectionProvider());

    #region Protect and TryUnprotect Tests

    [Fact]
    public void Protect_ThenTryUnprotect_ReturnsThePosition()
    {
        var protector = CreateProtector();
        var position = CreatePosition();

        var cursor = protector.Protect(Collection, position);
        var result = protector.TryUnprotect(Collection, cursor, out var actual);

        Assert.True(result);
        Assert.Equal(position, actual);
    }

    [Fact]
    public void TryUnprotect_TamperedCursor_ReturnsFalse()
    {
        var protector = CreateProtector();
        var cursor = protector.Protect(Collection, CreatePosition());
        var replacement = cursor[20] == 'A' ? 'B' : 'A';
        var tampered = string.Concat(cursor.AsSpan(0, 20), [replacement], cursor.AsSpan(21));

        var result = protector.TryUnprotect(Collection, tampered, out var actual);

        Assert.False(result);
        Assert.Null(actual);
    }

    [Fact]
    public void TryUnprotect_OtherCollection_ReturnsFalse()
    {
        var protector = CreateProtector();
        var cursor = protector.Protect(Collection, CreatePosition());

        var result = protector.TryUnprotect("ConsentEntity", cursor, out var actual);

        Assert.False(result);
        Assert.Null(actual);
    }

    [Fact]
    public void TryUnprotect_OtherKeyRing_ReturnsFalse()
    {
        var protector = CreateProtector();
        var otherProtector = CreateProtector();
        var cursor = protector.Protect(Collection, CreatePosition());

        var result = otherProtector.TryUnprotect(Collection, cursor, out var actual);

        Assert.False(result);
        Assert.Null(actual);
    }

    [Fact]
    public void TryUnprotect_MalformedCursor_ReturnsFalse()
    {
        var protector = CreateProtector();

        var result = protector.TryUnprotect(Collection, "!!", out var actual);

        Assert.False(result);
        Assert.Null(actual);
    }

    [Fact]
    public void Protect_Position_DoesNotAppearInTheCursor()
    {
        var protector = CreateProtector();
        var position = CreatePosition();

        var cursor = protector.Protect(Collection, position);
        var bytes = Base64Url.DecodeFromChars(cursor);

        Assert.True(bytes.AsSpan().IndexOf(position.AsSpan()) < 0);
    }

    #endregion
}
