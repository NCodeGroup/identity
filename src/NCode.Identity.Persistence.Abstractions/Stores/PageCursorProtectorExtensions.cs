// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Persistence.Stores;

/// <summary>
/// Provides extension methods for <see cref="IPageCursorProtector"/>. The extensions are the form rule of a surrogate
/// key's cursor position: the key as 16 big-endian bytes, and no key for anything else.
/// </summary>
[PublicAPI]
public static class PageCursorProtectorExtensions
{
    /// <summary>
    /// The length in bytes of a surrogate key.
    /// </summary>
    private const int KeyLength = 16;

    /// <param name="protector">The <see cref="IPageCursorProtector"/> that protects the cursor.</param>
    extension(IPageCursorProtector protector)
    {
        /// <summary>
        /// Protects a surrogate key as the position where a page ends and returns the opaque cursor.
        /// </summary>
        /// <param name="collection">The name of the collection that the cursor pages.</param>
        /// <param name="key">The surrogate key of the last item on the page.</param>
        /// <returns>The opaque cursor, which is safe to hand to a caller.</returns>
        [PublicAPI]
        public string ProtectKey(string collection, Guid key)
        {
            ArgumentNullException.ThrowIfNull(protector);

            Span<byte> position = stackalloc byte[KeyLength];
            key.TryWriteBytes(position, bigEndian: true, out _);
            return protector.Protect(collection, position);
        }

        /// <summary>
        /// Reads the surrogate key where the previous page ended from a cursor.
        /// </summary>
        /// <param name="collection">The name of the collection that the cursor pages.</param>
        /// <param name="cursor">The cursor that a caller passed back.</param>
        /// <returns>
        /// The surrogate key of the last item on the previous page, or <c>null</c> when the cursor is <c>null</c> or
        /// empty, cannot be unprotected, or does not hold a surrogate key.
        /// </returns>
        [PublicAPI]
        public Guid? UnprotectKey(string collection, string? cursor)
        {
            ArgumentNullException.ThrowIfNull(protector);

            if (string.IsNullOrEmpty(cursor))
            {
                return null;
            }

            Span<byte> position = stackalloc byte[KeyLength];
            if (
                !protector.TryUnprotect(collection, cursor, position, out var bytesWritten)
                || bytesWritten != KeyLength
            )
            {
                return null;
            }

            return new Guid(position, bigEndian: true);
        }
    }
}
