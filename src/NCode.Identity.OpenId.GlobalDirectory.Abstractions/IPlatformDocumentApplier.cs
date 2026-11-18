// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Applies the platform changes of the directory's change feed to the cell's platform row. The feed's position lives in
/// <see cref="IDirectoryFeedCheckpoint"/>; the platform row's sequence only makes a re-delivered document a no-op.
/// Register a custom implementation to replace the default behavior.
/// </summary>
[PublicAPI]
public interface IPlatformDocumentApplier
{
    /// <summary>
    /// Applies a published platform document: it writes the platform row's settings and announces the change so caches
    /// evict the settings. A document whose sequence the row already carries or exceeds changes nothing.
    /// </summary>
    /// <param name="document">The <see cref="PlatformDocument"/> to apply.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask ApplyAsync(PlatformDocument document, CancellationToken cancellationToken);
}
