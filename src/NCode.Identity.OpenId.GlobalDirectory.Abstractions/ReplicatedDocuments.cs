// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Contains the kinds of <see cref="ReplicatedDocument"/>.
/// </summary>
[PublicAPI]
public static class ReplicatedDocumentKinds
{
    /// <summary>
    /// Contains the kind of the platform's authority document: 'platform-authority'. Its key is empty.
    /// </summary>
    public const string PlatformAuthority = "platform-authority";

    /// <summary>
    /// Contains the kind of a team's authority document: 'team-authority'. Its key is the team identifier.
    /// </summary>
    public const string TeamAuthority = "team-authority";

    /// <summary>
    /// Contains the kind of the management keys document: 'management-keys'. Its key is empty.
    /// </summary>
    public const string ManagementKeys = "management-keys";
}

/// <summary>
/// Describes the content of an authority document: the role assignments at one node and, for the platform document,
/// the control-plane tenant's clients.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record AuthorityDocumentContent
{
    /// <summary>
    /// Gets the role assignments at the node.
    /// </summary>
    [JsonPropertyName("assignments")]
    public required IReadOnlyList<AuthorityAssignment> Assignments { get; init; }

    /// <summary>
    /// Gets the enabled clients of the control-plane tenant, which the platform document lists so that a cell knows
    /// offline whether a token's subject is a client acting for itself. It is empty in a team document.
    /// </summary>
    [JsonPropertyName("clients")]
    public IReadOnlyList<AuthorityClient> Clients { get; init; } = [];
}

/// <summary>
/// Describes one role held by a principal at a node.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record AuthorityAssignment
{
    /// <summary>
    /// Gets the identifier of the principal.
    /// </summary>
    [JsonPropertyName("principalId")]
    public required string PrincipalId { get; init; }

    /// <summary>
    /// Gets the name of the role.
    /// </summary>
    [JsonPropertyName("roleName")]
    public required string RoleName { get; init; }
}

/// <summary>
/// Describes one enabled client of the control-plane tenant.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed record AuthorityClient
{
    /// <summary>
    /// Gets the identifier of the principal that the client acts as.
    /// </summary>
    [JsonPropertyName("principalId")]
    public required string PrincipalId { get; init; }

    /// <summary>
    /// Gets the client identifier.
    /// </summary>
    [JsonPropertyName("clientId")]
    public required string ClientId { get; init; }
}

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
    public IReadOnlyList<string> RevokedKeyIds { get; init; } = [];
}

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
    /// Gets the size of the key in bits, or <c>null</c> when the key has no size.
    /// </summary>
    [JsonPropertyName("keySizeBits")]
    public int? KeySizeBits { get; init; }

    /// <summary>
    /// Gets the base64url public material of the key.
    /// </summary>
    [JsonPropertyName("publicKey")]
    public required string PublicKey { get; init; }

    /// <summary>
    /// Gets when the key becomes active, or <c>null</c> for a key that is active from the start.
    /// </summary>
    [JsonPropertyName("activatesWhen")]
    public DateTimeOffset? ActivatesWhen { get; init; }

    /// <summary>
    /// Gets when the key is retired, or <c>null</c> for a key that is not scheduled for retirement.
    /// </summary>
    [JsonPropertyName("retiresWhen")]
    public DateTimeOffset? RetiresWhen { get; init; }

    /// <summary>
    /// Gets when the key expires, or <c>null</c> for a key that does not expire.
    /// </summary>
    [JsonPropertyName("expiresWhen")]
    public DateTimeOffset? ExpiresWhen { get; init; }
}
