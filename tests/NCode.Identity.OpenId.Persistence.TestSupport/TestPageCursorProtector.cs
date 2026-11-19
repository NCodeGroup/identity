// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence;

/// <summary>
/// Provides an <see cref="IPageCursorProtector"/> for tests that protects a cursor with a key ring of its own that
/// lives in memory for the lifetime of the process.
/// </summary>
public sealed class TestPageCursorProtector : IPageCursorProtector
{
    private const string Purpose = "Test.PageCursor";

    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static TestPageCursorProtector Instance { get; } = new();

    private IDataProtectionProvider Provider { get; } = new EphemeralDataProtectionProvider();

    /// <inheritdoc />
    public string Protect(string collection, ReadOnlySpan<byte> position) =>
        Base64Url.EncodeToString(
            Provider.CreateProtector(Purpose, collection).Protect(position.ToArray())
        );

    /// <inheritdoc />
    public bool TryUnprotect(
        string collection,
        string cursor,
        Span<byte> destination,
        out int bytesWritten
    )
    {
        bytesWritten = 0;

        if (!Base64Url.IsValid(cursor))
            return false;

        var protectedBytes = Base64Url.DecodeFromChars(cursor);

        try
        {
            var position = Provider.CreateProtector(Purpose, collection).Unprotect(protectedBytes);

            if (position.Length > destination.Length)
                return false;

            position.CopyTo(destination);
            bytesWritten = position.Length;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
