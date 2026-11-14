// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Client.KeyRings;

/// <summary>
/// Configures one named client key ring that the host loads when it starts, from PEM files, keys that a key management
/// service holds, or both. Bound from the <c>{section}:{name}</c> configuration section, where <c>{section}</c> is the
/// section passed to <c>AddOpenIdClientKeyRings</c> and <c>{name}</c> is the key ring's name.
/// </summary>
// See ADR-1439.
[PublicAPI]
public sealed class ClientKeyRingOptions
{
    /// <summary>
    /// Gets or sets the PEM files that hold the key ring's private keys. Each entry is the path of a PEM-encoded EC or
    /// RSA private key, relative to the content root or absolute. A key's id is its file name without the extension.
    /// The configuration section is <c>{section}:{name}:PemFiles</c>.
    /// </summary>
    public List<string> PemFiles { get; set; } = [];

    /// <summary>
    /// Gets or sets the keys that a key management service holds, by key id. The configuration section is
    /// <c>{section}:{name}:RemoteKeys</c>.
    /// </summary>
    public Dictionary<string, ClientRemoteKeyOptions> RemoteKeys { get; set; } =
        new(StringComparer.Ordinal);
}
