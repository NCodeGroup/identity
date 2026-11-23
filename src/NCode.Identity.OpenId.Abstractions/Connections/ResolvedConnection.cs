// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Contains a connection of a tenant together with its registered strategy and its effective settings.
/// </summary>
// See ADR-1535.
[PublicAPI]
public sealed class ResolvedConnection
{
    /// <summary>
    /// Gets the persisted connection.
    /// </summary>
    public required PersistedConnection Connection { get; init; }

    /// <summary>
    /// Gets the registered strategy of the connection.
    /// </summary>
    public required IConnectionStrategy Strategy { get; init; }

    /// <summary>
    /// Gets the effective settings of the connection: the tenant's effective settings resolved through the
    /// connection's own settings document.
    /// </summary>
    public required IReadOnlySettingCollection Settings { get; init; }

    /// <summary>
    /// Gets the opaque identifier of the connection.
    /// </summary>
    public string ConnectionId => Connection.ConnectionId;

    /// <summary>
    /// Gets the name of the connection.
    /// </summary>
    public string Name => Connection.Name;
}
