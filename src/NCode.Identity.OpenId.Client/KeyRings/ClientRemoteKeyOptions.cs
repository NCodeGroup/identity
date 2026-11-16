// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Client.KeyRings;

/// <summary>
/// Configures one key of a client key ring that a key management service holds. Bound from the
/// <c>{section}:{name}:RemoteKeys:{kid}</c> configuration section, where <c>{section}</c> is the section passed to
/// <c>AddOpenIdClientKeyRings</c> and <c>{kid}</c> is the key's id. The remote key provider's row for the key decides
/// whether it signs or decrypts.
/// </summary>
// See ADR-1479.
[PublicAPI]
public sealed class ClientRemoteKeyOptions
{
    /// <summary>
    /// Gets or sets the name of the remote key provider that holds the key, such as <c>aws-kms</c>,
    /// <c>azure-key-vault</c> or <c>google-cloud-kms</c>. The configuration key is <c>Provider</c>.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the key management service's identifier for the key: an AWS KMS key ARN, an Azure Key Vault key
    /// version identifier or a Google Cloud KMS crypto key version name. The configuration key is <c>KeyId</c>.
    /// </summary>
    public string? KeyId { get; set; }

    /// <summary>
    /// Gets or sets the JOSE <c>alg</c> that the key signs or decrypts with. The configuration key is <c>Algorithm</c>.
    /// </summary>
    public string? Algorithm { get; set; }

    /// <summary>
    /// Gets or sets the key's secret type, <c>rsa</c> or <c>ecc</c>. The configuration key is <c>SecretType</c>.
    /// </summary>
    public string? SecretType { get; set; }

    /// <summary>
    /// Gets or sets the key's size in bits. The configuration key is <c>KeySizeBits</c>.
    /// </summary>
    public int KeySizeBits { get; set; }

    /// <summary>
    /// Gets or sets the JWK <c>crv</c> of an elliptic-curve key, and is unset for an RSA key. The configuration key is
    /// <c>Curve</c>.
    /// </summary>
    public string? Curve { get; set; }
}
