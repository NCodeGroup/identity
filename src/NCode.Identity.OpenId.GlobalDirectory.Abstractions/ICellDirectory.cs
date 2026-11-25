// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Reports, registers, retires and lists the cells of the platform. An implementation that cannot reach the directory
/// throws <see cref="DirectoryUnavailableException"/>. Register a custom implementation to replace the default
/// behavior.
/// </summary>
[PublicAPI]
public interface ICellDirectory
{
    /// <summary>
    /// Reports this cell's own descriptor to the directory, replacing the management URL the directory holds under the
    /// same name. Only a cell that the control-plane cell registered reports.
    /// </summary>
    /// <param name="descriptor">The <see cref="CellDescriptor"/> of this cell.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    /// <exception cref="CellDirectoryConflictException">The directory refuses the report.</exception>
    ValueTask ReportAsync(CellDescriptor descriptor, CancellationToken cancellationToken);

    /// <summary>
    /// Registers a cell, or replaces the key set of a registered cell. Only the control-plane cell registers cells.
    /// </summary>
    /// <param name="registration">The <see cref="CellRegistration"/> of the cell.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    /// <exception cref="CellDirectoryConflictException">The directory refuses the registration.</exception>
    ValueTask RegisterAsync(CellRegistration registration, CancellationToken cancellationToken);

    /// <summary>
    /// Retires a cell, so its name is never reissued. Only the control-plane cell retires cells.
    /// </summary>
    /// <param name="name">The name of the cell.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    /// <exception cref="CellDirectoryConflictException">The directory refuses the retirement.</exception>
    ValueTask RetireAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Lists every registered cell.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="CellDescriptor"/> of every registered cell.</returns>
    ValueTask<IReadOnlyList<CellDescriptor>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a registered cell by name.
    /// </summary>
    /// <param name="name">The name of the cell.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="CellDescriptor"/> of the cell, or <c>null</c> when no cell has the name.</returns>
    ValueTask<CellDescriptor?> GetOrDefaultAsync(string name, CancellationToken cancellationToken);
}
