// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ICellDirectory"/> of a cell that runs on its own. The directory lists the one cell that the
/// <see cref="CellOptions"/> describe, which is registered by being the cell. Reporting that cell writes nothing, and a
/// platform of one cell has no directory to register another cell in or retire one from.
/// </summary>
// See ADR-1411.
internal sealed class LocalCellDirectory(IOptions<CellOptions> cellOptions) : ICellDirectory
{
    private const string NoDirectoryDetail =
        "A platform of one cell has no directory that registers or retires cells.";

    private CellOptions CellOptions { get; } = cellOptions.Value;

    private CellDescriptor Descriptor =>
        new()
        {
            Name = CellOptions.Name,
            Region = CellOptions.Region,
            Kind = CellOptions.Kind,
            ManagementUrl = CellOptions.ManagementUrl,
            IsControlPlane = CellOptions.IsControlPlane,
            IsRegistered = true,
        };

    /// <inheritdoc />
    public ValueTask ReportAsync(CellDescriptor descriptor, CancellationToken cancellationToken)
    {
        // The cell's own options are the report.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RegisterAsync(
        CellRegistration registration,
        CancellationToken cancellationToken
    )
    {
        throw new CellDirectoryConflictException(NoDirectoryDetail);
    }

    /// <inheritdoc />
    public ValueTask RetireAsync(string name, CancellationToken cancellationToken)
    {
        throw new CellDirectoryConflictException(NoDirectoryDetail);
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
        var isMatch = string.Equals(descriptor.Name, name, StringComparison.OrdinalIgnoreCase);
        return ValueTask.FromResult<CellDescriptor?>(isMatch ? descriptor : null);
    }
}
