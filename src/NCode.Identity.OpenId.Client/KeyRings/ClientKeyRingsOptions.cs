// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Client.KeyRings;

/// <summary>
/// Holds the named client key rings that <c>AddOpenIdClientKeyRings</c> binds from a configuration section, and the
/// path of that section, which begins every validation failure and appears in the error about a key ring that is not
/// loaded.
/// </summary>
// See ADR-1439.
internal sealed class ClientKeyRingsOptions
{
    /// <summary>
    /// Gets or sets the key rings, by name.
    /// </summary>
    public Dictionary<string, ClientKeyRingOptions> KeyRings { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the configuration path of the section that the key rings are bound from, such as
    /// <c>KeyRings</c>.
    /// </summary>
    public string SectionPath { get; set; } = string.Empty;
}
