// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes one public key of the management keys document.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record ManagementKeyEntry
{
    /// <summary>
    /// Gets the key identifier.
    /// </summary>
    [JsonPropertyName("kid")]
    public required string KeyId { get; init; }

    /// <summary>
    /// Gets the type of the secret that holds the key.
    /// </summary>
    [JsonPropertyName("secretType")]
    public required string SecretType { get; init; }

    /// <summary>
    /// Gets the signing algorithm of the key.
    /// </summary>
    [JsonPropertyName("algorithm")]
    public required string Algorithm { get; init; }

    /// <summary>
    /// Gets the use of the key.
    /// </summary>
    [JsonPropertyName("use")]
    public required string Use { get; init; }

    /// <summary>
    /// Gets the size of the key in bits.
    /// </summary>
    [JsonPropertyName("keySizeBits")]
    public required int KeySizeBits { get; init; }

    /// <summary>
    /// Gets the base64url public material of the key.
    /// </summary>
    [JsonPropertyName("publicKey")]
    public required string PublicKey { get; init; }

    /// <summary>
    /// Gets when the key becomes active.
    /// </summary>
    [JsonPropertyName("activatesWhen")]
    public required DateTimeOffset ActivatesWhen { get; init; }

    /// <summary>
    /// Gets when the key is retired.
    /// </summary>
    [JsonPropertyName("retiresWhen")]
    public required DateTimeOffset RetiresWhen { get; init; }

    /// <summary>
    /// Gets when the key expires.
    /// </summary>
    [JsonPropertyName("expiresWhen")]
    public required DateTimeOffset ExpiresWhen { get; init; }
}
