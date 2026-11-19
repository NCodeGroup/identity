// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
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
        [NotNullWhen(true)] out byte[]? position
    )
    {
        position = null;

        if (!Base64Url.IsValid(cursor))
            return false;

        var protectedBytes = Base64Url.DecodeFromChars(cursor);

        try
        {
            position = DataProtectionProvider
                .CreateProtector(Purpose, collection)
                .Unprotect(protectedBytes);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
