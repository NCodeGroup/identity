// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Decides which connections a client allows, through its <c>allowed_connections</c> setting.
/// </summary>
// See ADR-1535.
[PublicAPI]
public static class ConnectionAllowances
{
    /// <summary>
    /// Determines whether a client allows a connection.
    /// </summary>
    /// <param name="clientSettings">The client's effective settings.</param>
    /// <param name="connectionName">The name of the connection, or <see langword="null"/> for no connection.</param>
    /// <returns>
    /// <see langword="true"/> when the client's <c>allowed_connections</c> setting is unset, which imposes no
    /// restriction, or lists the connection's name, compared case-insensitively as the connection store compares it; a
    /// set list that is empty allows no connection.
    /// </returns>
    public static bool IsAllowed(IReadOnlySettingCollection clientSettings, string? connectionName)
    {
        ArgumentNullException.ThrowIfNull(clientSettings);

        return !clientSettings.TryGetValue(OpenIdSettingKeys.AllowedConnections, out var allowed)
            || (
                connectionName is not null
                && allowed.Contains(connectionName, StringComparer.OrdinalIgnoreCase)
            );
    }
}
