// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

/// <summary>
/// Provides a default implementation of <see cref="IPageCursorProtector"/> that protects a cursor with ASP.NET Core
/// Data Protection, using a protector whose purpose names the collection that the cursor pages.
/// </summary>
// See ADR-1517.
internal sealed class DefaultPageCursorProtector(IDataProtectionProvider dataProtectionProvider)
    : IPageCursorProtector
{
    private const string Purpose = "NCode.Identity.Persistence.PageCursor";

    private IDataProtectionProvider DataProtectionProvider { get; } = dataProtectionProvider;

    /// <inheritdoc />
    public string Protect(string collection, ReadOnlySpan<byte> position) =>
        Base64Url.EncodeToString(
            DataProtectionProvider.CreateProtector(Purpose, collection).Protect(position.ToArray())
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
            var position = DataProtectionProvider
                .CreateProtector(Purpose, collection)
                .Unprotect(protectedBytes);

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
