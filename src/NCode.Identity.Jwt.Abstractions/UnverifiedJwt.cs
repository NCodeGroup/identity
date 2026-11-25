// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.Jose;
using NCode.Identity.Json;

namespace NCode.Identity.Jwt;

/// <summary>
/// Reads claims of a compact JWT without verifying it, to find the keys or the validator that verify it.
/// </summary>
[PublicAPI]
public static class UnverifiedJwt
{
    private const int SegmentCount = 3;
    private const int HeaderSegment = 0;
    private const int PayloadSegment = 1;

    /// <summary>
    /// Reads the <c>iss</c> claim of a compact JWT without verifying it.
    /// </summary>
    /// <param name="compactJwt">The compact JWT, which has exactly three parts separated by periods.</param>
    /// <param name="issuer">The <c>iss</c> claim when the method returns <c>true</c>; otherwise empty.</param>
    /// <returns><c>true</c> when the payload is a JSON object with a non-empty string <c>iss</c>.</returns>
    public static bool TryReadIssuer(string? compactJwt, out string issuer) =>
        TryReadStringMember(compactJwt, PayloadSegment, JoseClaimNames.Payload.Iss, out issuer);

    /// <summary>
    /// Reads the <c>kid</c> header of a compact JWT without verifying it.
    /// </summary>
    /// <param name="compactJwt">The compact JWT, which has exactly three parts separated by periods.</param>
    /// <param name="keyId">The <c>kid</c> header when the method returns <c>true</c>; otherwise empty.</param>
    /// <returns><c>true</c> when the header is a JSON object with a non-empty string <c>kid</c>.</returns>
    public static bool TryReadKeyId(string? compactJwt, out string keyId) =>
        TryReadStringMember(compactJwt, HeaderSegment, JoseClaimNames.Header.Kid, out keyId);

    /// <summary>
    /// Reads a non-empty string member of the JSON object that one segment of a compact JWT encodes.
    /// </summary>
    /// <param name="compactJwt">The compact JWT, which has exactly three parts separated by periods.</param>
    /// <param name="segmentIndex">The zero-based index of the header or payload segment.</param>
    /// <param name="memberName">The name of the member to read.</param>
    /// <param name="value">The member when the method returns <c>true</c>; otherwise empty.</param>
    /// <returns><c>true</c> when the segment is a JSON object with a non-empty string member of that name.</returns>
    private static bool TryReadStringMember(
        string? compactJwt,
        int segmentIndex,
        string memberName,
        out string value
    )
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(compactJwt))
            return false;

        // One spare slot: extra segments would otherwise fold into the last range and pass.
        var span = compactJwt.AsSpan();
        Span<Range> ranges = stackalloc Range[SegmentCount + 1];
        if (span.Split(ranges, '.') != SegmentCount)
            return false;

        try
        {
            var segment = span[ranges[segmentIndex]];
            using var lease = StrictBase64Url.RentDecoded(
                segment,
                isSensitive: false,
                out ReadOnlySpan<byte> bytes
            );
            var json = JsonElement.Parse(bytes);
            if (
                json.ValueKind == JsonValueKind.Object
                && json.TryGetProperty(memberName, out var element)
                && element.ValueKind == JsonValueKind.String
                && element.GetString() is { Length: > 0 } text
            )
            {
                value = text;
                return true;
            }
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }

        return false;
    }
}
