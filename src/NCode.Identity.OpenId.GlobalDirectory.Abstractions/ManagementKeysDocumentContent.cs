// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the content of the management keys document: the public keys that verify management tokens and the
/// identifiers of the keys that are revoked.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record ManagementKeysDocumentContent
{
    /// <summary>
    /// Gets the public keys, including keys that activate later.
    /// </summary>
    [JsonPropertyName("keys")]
    public required IReadOnlyList<ManagementKeyEntry> Keys { get; init; }

    /// <summary>
    /// Gets the identifiers of the revoked keys, which no later document returns.
    /// </summary>
    [JsonPropertyName("revokedKeyIds")]
    public required IReadOnlyList<string> RevokedKeyIds { get; init; }
}
