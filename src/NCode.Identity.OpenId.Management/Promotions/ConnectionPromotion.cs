// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Persistence.DataContracts;

namespace NCode.Identity.OpenId.Management.Promotions;

/// <summary>
/// Contains what a promotion does for one connection of the source tenant's configuration document: the target it
/// reaches, the settings document it writes, and the errors that refuse it.
/// </summary>
// See ADR-1535.
internal sealed class ConnectionPromotion
{
    /// <summary>
    /// Gets the connection of the source tenant's configuration document.
    /// </summary>
    public required TenantConfigurationConnection Source { get; init; }

    /// <summary>
    /// Gets the connection of the target tenant that has the source's name, or <c>null</c> when the promotion creates
    /// it.
    /// </summary>
    public required PersistedConnection? Target { get; init; }

    /// <summary>
    /// Gets the settings document the connection holds once promoted.
    /// </summary>
    public required PromotedSettings Settings { get; init; }

    /// <summary>
    /// Gets the action the promotion takes, one of the <c>TenantPromotionActions</c> values.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// Gets the reasons the promotion refuses the connection.
    /// </summary>
    public required List<string> Errors { get; init; }

    /// <summary>
    /// Gets a value indicating whether the settings document the target holds differs from the promoted document.
    /// </summary>
    public bool IsSettingsChanged { get; init; }
}
