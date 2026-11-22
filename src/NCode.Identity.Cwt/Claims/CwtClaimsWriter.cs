// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Cose;
using NCode.Identity.Cose.Keys;
using NCode.Identity.Cwt.Issuance;

namespace NCode.Identity.Cwt.Claims;

/// <summary>
/// Builds the claims set of a CBOR Web Token (CWT) to issue.
/// </summary>
// See ADR-1410.
internal static class CwtClaimsWriter
{
    /// <summary>
    /// Builds the claims from the issuance parameters. Every NumericDate is written as whole seconds.
    /// </summary>
    /// <exception cref="ArgumentException">A claim is invalid.</exception>
    public static Dictionary<CoseLabel, CoseValue> Build(
        CwtIssuanceParameters parameters,
        DateTimeOffset now,
        ICoseSerializer serializer,
        ICoseKeyConverter keyConverter
    )
    {
        var claims = new Dictionary<CoseLabel, CoseValue>();
        if (parameters.Issuer is { } issuer)
            claims[CwtClaimKeys.Issuer] = CoseValue.FromTextString(issuer);

        if (parameters.Subject is { } subject)
            claims[CwtClaimKeys.Subject] = CoseValue.FromTextString(subject);

        if (parameters.Audiences.Count == 1)
            claims[CwtClaimKeys.Audience] = CoseValue.FromTextString(parameters.Audiences[0]);
        else if (parameters.Audiences.Count > 1)
            claims[CwtClaimKeys.Audience] = CoseValue.FromTextStrings(parameters.Audiences);

        if (parameters.ExpiresAt is { } expiresAt)
        {
            var expirationSeconds = expiresAt.ToUnixTimeSeconds();
            claims[CwtClaimKeys.ExpirationTime] = CoseValue.FromInt64(expirationSeconds);
        }

        if (parameters.NotBefore is { } notBefore)
        {
            var notBeforeSeconds = notBefore.ToUnixTimeSeconds();
            claims[CwtClaimKeys.NotBefore] = CoseValue.FromInt64(notBeforeSeconds);
        }

        var issuedAtSeconds = (parameters.IssuedAt ?? now).ToUnixTimeSeconds();
        claims[CwtClaimKeys.IssuedAt] = CoseValue.FromInt64(issuedAtSeconds);

        if (parameters.CwtId is { } id)
            claims[CwtClaimKeys.CwtId] = CoseValue.FromByteString(id.Span);

        if (parameters.ConfirmationKey is { } confirmationKey)
        {
            var encodedKey = keyConverter.WritePublicKey(confirmationKey);
            var confirmation = new Dictionary<CoseLabel, CoseValue>
            {
                [CwtConfirmationKeys.CoseKey] = CoseValue.FromEncodedValue(encodedKey),
            };
            var encodedConfirmation = serializer.EncodeMap(confirmation);
            claims[CwtClaimKeys.Confirmation] = CoseValue.FromEncodedValue(encodedConfirmation);
        }

        foreach (var (label, value) in parameters.AdditionalClaims)
            claims[label] = value;

        return claims;
    }
}
