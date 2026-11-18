// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ICellDirectory"/> of a cell that runs on its own. The directory lists the one cell that the
/// <see cref="CellOptions"/> describe, so registering that cell writes nothing.
/// </summary>
// See ADR-1411.
internal sealed class LocalCellDirectory(IOptions<CellOptions> cellOptions) : ICellDirectory
{
    private CellOptions CellOptions { get; } = cellOptions.Value;

    private CellDescriptor Descriptor =>
        new()
        {
            Name = CellOptions.Name,
            Region = CellOptions.Region,
            Kind = CellOptions.Kind,
            ManagementUrl = CellOptions.ManagementUrl,
            IsControlPlane = CellOptions.IsControlPlane,
        };

    /// <inheritdoc />
    public ValueTask RegisterAsync(CellDescriptor descriptor, CancellationToken cancellationToken)
    {
        // The cell's own options are the registration.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<CellDescriptor>> ListAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlyList<CellDescriptor>>([Descriptor]);
    }

    /// <inheritdoc />
    public ValueTask<CellDescriptor?> GetOrDefaultAsync(
        string name,
        CancellationToken cancellationToken
    )
    {
        var descriptor = Descriptor;
        return ValueTask.FromResult<CellDescriptor?>(
            string.Equals(descriptor.Name, name, StringComparison.OrdinalIgnoreCase)
                ? descriptor
                : null
        );
    }
}
