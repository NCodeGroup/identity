// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Host.Database.Facades;
using NCode.Identity.OpenId.Host.Database.Logging;

namespace NCode.Identity.OpenId.Host.Database;

/// <summary>
/// Checks, when the host starts and before it accepts traffic, that the Postgres role the host runs as is subject to
/// row-level security (see <see cref="RuntimeRolePosture"/>). Under the deployment posture a violation stops the host;
/// under <c>LocalDevelopment:Enabled</c> it logs one warning and the host starts.
/// </summary>
/// <param name="reader">The reader of the posture of the role the host connects as.</param>
/// <param name="localDevelopmentMode">
/// The mode that decides whether a role that is not subject to row-level security only warns
/// (<c>LocalDevelopment:Enabled</c>) instead of stopping the host.
/// </param>
/// <param name="logger">The logger that receives the warning.</param>
// See ADR-1370.
internal class RuntimeRolePostureCheck(
    IRuntimeRolePostureReader reader,
    LocalDevelopmentMode localDevelopmentMode,
    ILogger<RuntimeRolePostureCheck> logger
) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var violations = await ReadViolationsAsync(cancellationToken);
        if (violations.Count == 0)
            return;

        var joined = string.Join("; ", violations);

        if (localDevelopmentMode.Enabled)
        {
            logger.RuntimeRolePostureViolated(joined, RuntimeRolePosture.Remedy);
            return;
        }

        throw new InvalidOperationException(
            $"The runtime database role is not subject to row-level security: {joined}. {RuntimeRolePosture.Remedy}"
        );
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Reads the posture of the role that the host runs as and judges it.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// The reasons the role is not subject to row-level security, which is empty when the role is subject to it.
    /// </returns>
    /// <exception cref="InvalidOperationException">The database has no row for the connected role.</exception>
    private async ValueTask<IReadOnlyList<string>> ReadViolationsAsync(
        CancellationToken cancellationToken
    )
    {
        var row = await reader.ReadAsync(cancellationToken);
        if (row is not { } posture)
        {
            throw new InvalidOperationException(
                "The runtime database role was not found in pg_roles, so its row-level security posture cannot be judged."
            );
        }

        return RuntimeRolePosture.Evaluate(
            isSuperuser: posture.IsSuperuser,
            bypassesRls: posture.BypassesRls,
            ownsTenantTables: posture.OwnsTenantTables
        );
    }
}
