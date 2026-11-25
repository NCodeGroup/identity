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
    /// <summary>
    /// Reads the <c>iss</c> claim of a compact JWT without verifying it.
    /// </summary>
    /// <param name="compactJwt">The compact JWT, which has exactly three parts separated by periods.</param>
    /// <param name="issuer">The <c>iss</c> claim when the method returns <c>true</c>; otherwise empty.</param>
    /// <returns><c>true</c> when the payload is a JSON object with a non-empty string <c>iss</c>.</returns>
    public static bool TryReadIssuer(string? compactJwt, out string issuer)
    {
        issuer = string.Empty;
        if (string.IsNullOrEmpty(compactJwt))
            return false;

        var parts = compactJwt.Split('.');
        if (parts.Length != 3)
            return false;

        try
        {
            var payloadBytes = StrictBase64Url.Decode(parts[1]);
            var payload = JsonElement.Parse(payloadBytes);
            if (
                payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty(JoseClaimNames.Payload.Iss, out var element)
                && element.ValueKind == JsonValueKind.String
                && element.GetString() is { Length: > 0 } value
            )
            {
                issuer = value;
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
