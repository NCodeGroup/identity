// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Contracts.Capabilities;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.Endpoints.Capabilities;

/// <summary>
/// Contributes the <c>cell</c> section to the management capabilities document, naming the cell that serves the
/// management API, its region and whether it is the control-plane cell, so clients can discover which cell answers
/// the platform and team management routes.
/// </summary>
internal sealed class CellCapabilitiesProvider(IOptions<CellOptions> cellOptions)
    : ICommandHandler<DiscoverManagementCapabilitiesCommand>
{
    /// <summary>
    /// The capability section name under which the cell capability is published.
    /// </summary>
    public const string SectionName = "cell";

    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DiscoverManagementCapabilitiesCommand command,
        CancellationToken cancellationToken
    )
    {
        var cell = new CellCapabilityResource
        {
            Name = CellOptions.Name,
            Region = CellOptions.Region,
            IsControlPlane = CellOptions.IsControlPlane,
        };

        // First contributor wins, so a replacement provider registered earlier is not overwritten.
        command.Capabilities.TryAdd(SectionName, cell);

        return ValueTask.CompletedTask;
    }
}
