// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains a signing secret of an audit stream as the store keeps it: the secret's identifier, its value protected
/// under the tenant's master key, and the time after which the stream no longer signs with it.
/// </summary>
// See ADR-1534.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedAuditStreamSigningSecret
{
    /// <summary>
    /// Gets the identifier of the secret, unique within its stream.
    /// </summary>
    public required string SecretId { get; init; }

    /// <summary>
    /// Gets or sets the secret's value, protected under the tenant's master key.
    /// </summary>
    [MaxLength(SecretMaxLengths.EncodedValue)]
    public required string ProtectedValue { get; set; }

    /// <summary>
    /// Gets the time at which the secret was created.
    /// </summary>
    public required DateTimeOffset CreatedTime { get; init; }

    /// <summary>
    /// Gets or sets the time after which the stream no longer signs with the secret, or <see langword="null"/> while
    /// the secret is the stream's current one.
    /// </summary>
    public DateTimeOffset? ExpiryTime { get; set; }
}
