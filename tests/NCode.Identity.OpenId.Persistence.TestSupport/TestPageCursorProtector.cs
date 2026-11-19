// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Collections.Concurrent;
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

    private ConcurrentDictionary<string, IDataProtector> Protectors { get; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the protector for a collection, creating it on first use.
    /// </summary>
    private IDataProtector GetProtector(string collection) =>
        Protectors.GetOrAdd(
            collection,
            static (name, provider) => provider.CreateProtector(Purpose, name),
            Provider
        );

    /// <inheritdoc />
    public string Protect(string collection, ReadOnlySpan<byte> position)
    {
        var protector = GetProtector(collection);
        var plaintext = position.ToArray();
        var protectedBytes = protector.Protect(plaintext);
        return Base64Url.EncodeToString(protectedBytes);
    }

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

        var protector = GetProtector(collection);
        var protectedBytes = Base64Url.DecodeFromChars(cursor);

        try
        {
            var position = protector.Unprotect(protectedBytes);

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
