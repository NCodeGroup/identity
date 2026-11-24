// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.Persistence;

namespace NCode.Identity.OpenId.Persistence.DataContracts;

/// <summary>
/// Contains the data for a management key that the cell refuses, whether the control plane revoked it or an operator
/// revoked it offline in the cell.
/// </summary>
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class PersistedRevokedManagementKey
{
    /// <summary>
    /// Specifies the <see cref="Source"/> of a key that the control plane revoked.
    /// </summary>
    public const string ControlPlaneSource = "control-plane";

    /// <summary>
    /// Specifies the <see cref="Source"/> of a key that an operator revoked offline in the cell.
    /// </summary>
    public const string OfflineSource = "offline";

    /// <summary>
    /// Gets the identifier of the revoked key.
    /// </summary>
    [MaxLength(MaxLengths.ResourceId)]
    public required string KeyId { get; init; }

    /// <summary>
    /// Gets the instant at which the key was revoked.
    /// </summary>
    public required DateTimeOffset RevokedWhen { get; init; }

    /// <summary>
    /// Gets where the revocation came from, either <see cref="ControlPlaneSource"/> or <see cref="OfflineSource"/>.
    /// </summary>
    [MaxLength(MaxLengths.ResourceType)]
    public required string Source { get; init; }
}
