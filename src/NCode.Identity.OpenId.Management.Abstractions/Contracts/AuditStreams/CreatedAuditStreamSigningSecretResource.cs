// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Management.Contracts.AuditStreams;

// See ADR-1534.

/// <summary>
/// Represents a signing secret of an audit stream together with its value, returned once when the secret is created.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class CreatedAuditStreamSigningSecretResource
{
    /// <summary>
    /// Gets the opaque, server-generated public identifier for this signing secret.
    /// </summary>
    public required string SecretId { get; init; }

    /// <summary>
    /// Gets the <c>whsec_</c> secret, shown only in this response.
    /// </summary>
    public required string SigningSecret { get; init; }

    /// <summary>
    /// Gets the time this signing secret was created.
    /// </summary>
    public required DateTimeOffset CreatedTime { get; init; }
}
