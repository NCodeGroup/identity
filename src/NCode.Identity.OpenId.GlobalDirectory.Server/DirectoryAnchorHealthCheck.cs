// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// The readiness check that reports healthy once <see cref="DirectoryAnchorInitializer"/> has committed the platform's
/// trust anchor.
/// </summary>
internal sealed class DirectoryAnchorHealthCheck(DirectoryAnchorState state) : IHealthCheck
{
    /// <summary>
    /// The name of the readiness check, <c>directory-anchor</c>.
    /// </summary>
    internal const string Name = "directory-anchor";

    private DirectoryAnchorState State { get; } = state;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var result = State.IsInitialized
            ? HealthCheckResult.Healthy("The trust anchor is initialized.")
            : HealthCheckResult.Unhealthy("The trust anchor is not initialized.");
        return Task.FromResult(result);
    }
}
