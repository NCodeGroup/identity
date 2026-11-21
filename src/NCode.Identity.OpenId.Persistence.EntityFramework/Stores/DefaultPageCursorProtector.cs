// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using NCode.Identity.Json;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

/// <summary>
/// Provides a default implementation of <see cref="IPageCursorProtector"/> that protects a cursor with ASP.NET Core
/// Data Protection, using a protector whose purpose names the collection that the cursor pages. It keeps one protector
/// per collection.
/// </summary>
// See ADR-1517.
internal sealed class DefaultPageCursorProtector(IDataProtectionProvider dataProtectionProvider)
    : IPageCursorProtector
{
    private const string Purpose = "NCode.Identity.Persistence.PageCursor";

    private IDataProtectionProvider DataProtectionProvider { get; } = dataProtectionProvider;

    private ConcurrentDictionary<string, IDataProtector> Protectors { get; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the protector for a collection, creating it on first use.
    /// </summary>
    private IDataProtector GetProtector(string collection) =>
        Protectors.GetOrAdd(
            collection,
            static (name, provider) => provider.CreateProtector(Purpose, name),
            DataProtectionProvider
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

        if (!StrictBase64Url.IsValid(cursor))
            return false;

        var protector = GetProtector(collection);
        var protectedBytes = StrictBase64Url.Decode(cursor);

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
