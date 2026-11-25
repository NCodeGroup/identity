// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Registration;

namespace NCode.Identity.Server;

/// <summary>
/// Runs the <see cref="IStartupPrerequisite"/> services of a built host before an offline command works, so the
/// command, which never starts the host, works with the same services the running host does: the key rings that sign a
/// cell's client assertions are loaded, and a command that reserves a tenant through the global directory can
/// authenticate to it.
/// </summary>
// See ADR-1536 (decision 12).
internal static class HostCommandPrerequisites
{
    /// <summary>
    /// Runs every registered prerequisite, in registration order.
    /// </summary>
    /// <param name="services">The services of the built host.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the command.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public static async ValueTask PrepareAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var prerequisite in services.GetServices<IStartupPrerequisite>())
        {
            await prerequisite.PrepareAsync(cancellationToken);
        }
    }
}
