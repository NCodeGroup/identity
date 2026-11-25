// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Secrets.Collections;

namespace NCode.Identity.OpenId.Client.Options;

/// <summary>
/// Holds the named client key rings that <c>AddOpenIdClientKeyRings</c> binds from the top-level <c>KeyRings</c>
/// section of a configuration, and the path of that section, which begins every validation failure and appears in the
/// error about a key ring that is not loaded.
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
    /// Gets or sets the configuration path of the section that the key rings are bound from, such as <c>KeyRings</c>.
    /// </summary>
    public string SectionPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the names of the configured key rings that a keyed <see cref="ISecretKeyCollectionProvider"/>
    /// already provided when the key rings were added. A key ring is registered once, in code or in configuration, so
    /// validation refuses each of them.
    /// </summary>
    public HashSet<string> RegisteredInCode { get; set; } = new(StringComparer.Ordinal);
}
