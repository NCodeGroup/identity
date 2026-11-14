// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Directory;

/// <summary>
/// Lists the cells of the platform. An implementation that cannot reach the directory throws
/// <see cref="DirectoryUnavailableException"/>. Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface ICellDirectory
{
    /// <summary>
    /// Registers this cell with the directory, replacing the descriptor the directory holds under the same name.
    /// </summary>
    /// <param name="descriptor">The <see cref="CellDescriptor"/> of this cell.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RegisterAsync(CellDescriptor descriptor, CancellationToken cancellationToken);

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
