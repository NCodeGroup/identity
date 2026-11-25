// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Wakes the publisher of the directory outbox after a commit wrote outbox entries. Register a custom implementation to
/// replace the default behavior.
/// </summary>
[PublicAPI]
public interface IDirectoryOutboxSignal
{
    /// <summary>
    /// Waits until the signal is raised or a time has passed.
    /// </summary>
    /// <param name="timeout">The longest time to wait.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Raises the signal, which wakes a waiting publisher or the next wait.
    /// </summary>
    void Notify();
}
