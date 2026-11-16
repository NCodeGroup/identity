// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.Secrets.Options;
using NCode.Identity.SharedState.Options;

namespace NCode.Identity.Server.Cells;

/// <summary>
/// Scopes the shared-state and key management service adapters to the cell, which the server composes and the adapters
/// do not know. Post-configuration makes <see cref="CellOptions.Name"/> the <see cref="SharedStateOptions.Deployment"/>
/// that a shared adapter keeps the cell's state under, and <see cref="CellOptions.CloudRegions"/> the
/// <see cref="KeyResidencyOptions.CloudRegions"/> that a key management service adapter keeps the cell's keys in. A
/// cell without a name leaves the deployment as configured, and a cell without cloud regions leaves the key residency
/// as configured.
/// </summary>
/// <param name="cellOptionsAccessor">The options that name the cell and its cloud regions.</param>
// See ADR-1164 and ADR-1235.
internal sealed class CellScopeOptionsSetup(IOptions<CellOptions> cellOptionsAccessor)
    : IPostConfigureOptions<SharedStateOptions>,
        IPostConfigureOptions<KeyResidencyOptions>
{
    /// <summary>
    /// Gets the options that name the cell and its cloud regions.
    /// </summary>
    private IOptions<CellOptions> CellOptionsAccessor { get; } = cellOptionsAccessor;

    /// <inheritdoc />
    public void PostConfigure(string? name, SharedStateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var cellName = CellOptionsAccessor.Value.Name;
        if (!string.IsNullOrEmpty(cellName))
            options.Deployment = cellName;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, KeyResidencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (CellOptionsAccessor.Value.CloudRegions is { Length: > 0 } cloudRegions)
            options.CloudRegions = [.. cloudRegions];
    }
}
