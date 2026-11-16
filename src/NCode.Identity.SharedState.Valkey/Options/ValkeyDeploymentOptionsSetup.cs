// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.SharedState.Options;

namespace NCode.Identity.SharedState.Valkey.Options;

/// <summary>
/// Appends the deployment that <see cref="SharedStateOptions.Deployment"/> names and <c>:</c> to the configured
/// <see cref="ValkeyOptions.KeyPrefix"/>, so every Valkey key and channel starts <c>{KeyPrefix}{deployment}:</c> and
/// deployments that share one Valkey server never read or write one another's state. The identity server names its
/// cell, so the default prefix gives <c>ncode-identity:{cell}:</c>. Like the prefix, the deployment holds no braces; a
/// cell's name holds only lowercase letters, digits and hyphens. A composition that names no deployment keeps the
/// prefix as configured.
/// </summary>
/// <param name="sharedStateOptionsAccessor">The options that name the deployment.</param>
// See ADR-1164.
internal sealed class ValkeyDeploymentOptionsSetup(
    IOptions<SharedStateOptions> sharedStateOptionsAccessor
) : IPostConfigureOptions<ValkeyOptions>
{
    /// <summary>
    /// Gets the options that name the deployment.
    /// </summary>
    private IOptions<SharedStateOptions> SharedStateOptionsAccessor { get; } =
        sharedStateOptionsAccessor;

    /// <inheritdoc />
    public void PostConfigure(string? name, ValkeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var deployment = SharedStateOptionsAccessor.Value.Deployment;
        if (!string.IsNullOrEmpty(deployment))
            options.KeyPrefix = $"{options.KeyPrefix}{deployment}:";
    }
}
