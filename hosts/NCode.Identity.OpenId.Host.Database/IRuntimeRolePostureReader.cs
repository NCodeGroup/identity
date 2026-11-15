// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Reads the row-level security posture of the Postgres role the host runs as.
/// </summary>
internal interface IRuntimeRolePostureReader
{
    /// <summary>
    /// Reads the posture of the connected role.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The role's posture, or <see langword="null"/> when the database has no row for the role.</returns>
    ValueTask<RuntimeRolePostureRow?> ReadAsync(CancellationToken cancellationToken);
}
