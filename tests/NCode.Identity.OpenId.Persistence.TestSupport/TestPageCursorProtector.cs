// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
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
        [NotNullWhen(true)] out byte[]? position
    )
    {
        position = null;

        if (!Base64Url.IsValid(cursor))
            return false;

        var protectedBytes = Base64Url.DecodeFromChars(cursor);

        try
        {
            position = Provider.CreateProtector(Purpose, collection).Unprotect(protectedBytes);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
