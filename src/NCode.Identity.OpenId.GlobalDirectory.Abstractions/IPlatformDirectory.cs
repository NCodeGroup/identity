// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Publishes the platform's settings document to the cells. An implementation that cannot reach the directory throws
/// <see cref="DirectoryUnavailableException"/>. Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface IPlatformDirectory
{
    /// <summary>
    /// Publishes the platform document, which every cell mirrors into its platform row.
    /// </summary>
    /// <param name="document">The <see cref="PlatformDocument"/> to publish.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask PublishAsync(PlatformDocument document, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the published platform document.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="PlatformDocument"/>, or <c>null</c> when none is published.</returns>
    ValueTask<PlatformDocument?> GetOrDefaultAsync(CancellationToken cancellationToken);
}
