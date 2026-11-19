// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Persistence.Stores;

/// <summary>
/// Protects the position where a keyset page ends into the opaque continuation cursor that a caller passes back, and
/// reads the position again.
/// </summary>
[PublicAPI]
public interface IPageCursorProtector
{
    /// <summary>
    /// Protects the position where a page ends into an opaque, authenticated cursor.
    /// </summary>
    /// <param name="collection">The name of the collection that the cursor pages. The cursor is bound to it.</param>
    /// <param name="position">The position where the page ends.</param>
    /// <returns>The opaque cursor, which is safe to hand to a caller.</returns>
    string Protect(string collection, ReadOnlySpan<byte> position);

    /// <summary>
    /// Reads the position from a cursor into a buffer of the caller.
    /// </summary>
    /// <param name="collection">The name of the collection that the cursor pages.</param>
    /// <param name="cursor">The cursor that a caller passed back.</param>
    /// <param name="destination">The buffer that receives the position where the previous page ended.</param>
    /// <param name="bytesWritten">
    /// When this method returns <c>true</c>, the number of bytes written to <paramref name="destination"/>; otherwise
    /// <c>0</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> when the cursor holds a position that fits in <paramref name="destination"/>; <c>false</c> when the
    /// cursor is malformed, tampered with, protected for another collection or protected under another key, or when the
    /// position is longer than <paramref name="destination"/>.
    /// </returns>
    bool TryUnprotect(
        string collection,
        string cursor,
        Span<byte> destination,
        out int bytesWritten
    );
}
