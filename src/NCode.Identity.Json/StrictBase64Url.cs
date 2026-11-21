// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Buffers;

namespace NCode.Identity.Json;

/// <summary>
/// Decodes only the canonical unpadded base64url of RFC 4648 section 5 that JOSE requires (RFC 7515 section 2). Text
/// with padding, whitespace, any other character, a length with a remainder of one character or a final character whose
/// unused bits are not zero is refused before anything is decoded, so a byte string has exactly one spelling.
/// </summary>
// See ADR-1266.
[SuppressMessage(
    "ApiDesign",
    "RS0030:Do not use banned APIs",
    Justification = "StrictBase64Url is the family's one base64url decoder and the only type allowed to call the banned decode members; it refuses what is not strict, then runs the .NET decoder."
)]
[PublicAPI]
public static class StrictBase64Url
{
    private const string InvalidMessage =
        "The text is not strict base64url: unpadded, canonical and only of the base64url alphabet.";

    private static SearchValues<char> Alphabet { get; } =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    private static SearchValues<byte> Utf8Alphabet { get; } =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"u8);

    /// <summary>
    /// Determines whether <paramref name="text"/> is strict base64url.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <returns><see langword="true"/> when the text is unpadded, canonical and of the base64url alphabet.</returns>
    public static bool IsValid(ReadOnlySpan<char> text) =>
        !text.ContainsAnyExcept(Alphabet) && Base64Url.IsValid(text);

    /// <summary>
    /// Determines whether the UTF-8 <paramref name="utf8Text"/> is strict base64url.
    /// </summary>
    /// <param name="utf8Text">The UTF-8 text to check.</param>
    /// <returns><see langword="true"/> when the text is unpadded, canonical and of the base64url alphabet.</returns>
    public static bool IsValid(ReadOnlySpan<byte> utf8Text) =>
        !utf8Text.ContainsAnyExcept(Utf8Alphabet) && Base64Url.IsValid(utf8Text);

    /// <summary>
    /// Gets the exact number of bytes that unpadded base64url text of <paramref name="textLength"/> characters decodes
    /// to.
    /// </summary>
    /// <param name="textLength">The number of characters of the text.</param>
    /// <returns>The number of decoded bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="textLength"/> is negative.</exception>
    /// <exception cref="FormatException">The length leaves a remainder of one character, which no bytes encode
    /// to.</exception>
    public static int GetDecodedLength(int textLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textLength);

        if (textLength % 4 == 1)
        {
            throw new FormatException("The text length is not that of unpadded base64url.");
        }

        return Base64Url.GetMaxDecodedLength(textLength);
    }

    /// <summary>
    /// Decodes strict base64url <paramref name="text"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="text">The text to decode.</param>
    /// <param name="destination">The buffer that receives the bytes.</param>
    /// <param name="bytesWritten">The number of bytes written, or <c>0</c> when this method returns
    /// <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the text was decoded; <see langword="false"/> when the destination is too
    /// short.</returns>
    /// <exception cref="FormatException">The text is not strict base64url.</exception>
    public static bool TryDecode(
        ReadOnlySpan<char> text,
        Span<byte> destination,
        out int bytesWritten
    )
    {
        ThrowIfInvalid(text);

        if (destination.Length < GetDecodedLength(text.Length))
        {
            bytesWritten = 0;
            return false;
        }

        bytesWritten = Base64Url.DecodeFromChars(text, destination);
        return true;
    }

    /// <summary>
    /// Decodes strict base64url UTF-8 <paramref name="utf8Text"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="utf8Text">The UTF-8 text to decode.</param>
    /// <param name="destination">The buffer that receives the bytes.</param>
    /// <param name="bytesWritten">The number of bytes written, or <c>0</c> when this method returns
    /// <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the text was decoded; <see langword="false"/> when the destination is too
    /// short.</returns>
    /// <exception cref="FormatException">The text is not strict base64url.</exception>
    public static bool TryDecode(
        ReadOnlySpan<byte> utf8Text,
        Span<byte> destination,
        out int bytesWritten
    )
    {
        ThrowIfInvalid(utf8Text);

        if (destination.Length < GetDecodedLength(utf8Text.Length))
        {
            bytesWritten = 0;
            return false;
        }

        bytesWritten = Base64Url.DecodeFromUtf8(utf8Text, destination);
        return true;
    }

    /// <summary>
    /// Decodes strict base64url <paramref name="text"/> into a new array. The array is not zeroed, so use this overload
    /// for non-secret material only; use <c>RentDecoded</c> with <c>isSensitive: true</c> for secrets.
    /// </summary>
    /// <param name="text">The text to decode.</param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="FormatException">The text is not strict base64url.</exception>
    public static byte[] Decode(ReadOnlySpan<char> text)
    {
        ThrowIfInvalid(text);
        return Base64Url.DecodeFromChars(text);
    }

    /// <summary>
    /// Decodes strict base64url <paramref name="text"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="text">The text to decode.</param>
    /// <param name="destination">The writer that receives the bytes.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The text is not strict base64url; nothing is written.</exception>
    public static int Decode(ReadOnlySpan<char> text, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfInvalid(text);

        var span = destination.GetSpan(GetDecodedLength(text.Length));
        var written = Base64Url.DecodeFromChars(text, span);
        destination.Advance(written);
        return written;
    }

    /// <summary>
    /// Decodes strict base64url <paramref name="text"/> into a rented buffer. The decoded view is valid only until the
    /// returned lease is disposed.
    /// </summary>
    /// <param name="text">The text to decode.</param>
    /// <param name="isSensitive">Whether the rented buffer is pinned and zeroed when it is disposed.</param>
    /// <param name="bytes">The decoded bytes; valid only until the returned lease is disposed.</param>
    /// <returns>The lease that owns the buffer; the caller disposes it.</returns>
    /// <exception cref="FormatException">The text is not strict base64url.</exception>
    public static IMemoryOwner<byte> RentDecoded(
        ReadOnlySpan<char> text,
        bool isSensitive,
        out ReadOnlySpan<byte> bytes
    )
    {
        ThrowIfInvalid(text);
        var lease = BinaryTextEncoding.RentDecoded(
            text,
            BinaryTextFormat.Base64Url,
            isSensitive,
            out Span<byte> decoded
        );
        bytes = decoded;
        return lease;
    }

    /// <summary>
    /// Decodes strict base64url <paramref name="text"/> into a rented buffer. The decoded view is valid only until the
    /// returned lease is disposed.
    /// </summary>
    /// <param name="text">The text to decode.</param>
    /// <param name="isSensitive">Whether the rented buffer is pinned and zeroed when it is disposed.</param>
    /// <param name="bytes">The decoded bytes; valid only until the returned lease is disposed.</param>
    /// <returns>The lease that owns the buffer; the caller disposes it.</returns>
    /// <exception cref="FormatException">The text is not strict base64url.</exception>
    public static IMemoryOwner<byte> RentDecoded(
        ReadOnlySpan<char> text,
        bool isSensitive,
        out ReadOnlyMemory<byte> bytes
    )
    {
        ThrowIfInvalid(text);
        var lease = BinaryTextEncoding.RentDecoded(
            text,
            BinaryTextFormat.Base64Url,
            isSensitive,
            out Memory<byte> decoded
        );
        bytes = decoded;
        return lease;
    }

    private static void ThrowIfInvalid(ReadOnlySpan<char> text)
    {
        if (!IsValid(text))
        {
            throw new FormatException(InvalidMessage);
        }
    }

    private static void ThrowIfInvalid(ReadOnlySpan<byte> utf8Text)
    {
        if (!IsValid(utf8Text))
        {
            throw new FormatException(InvalidMessage);
        }
    }
}
