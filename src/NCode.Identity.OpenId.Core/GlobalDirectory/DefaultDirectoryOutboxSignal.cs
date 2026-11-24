// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="IDirectoryOutboxSignal"/>, a semaphore of one that coalesces every
/// notification raised while no publisher waits into one wake-up.
/// </summary>
internal sealed class DefaultDirectoryOutboxSignal : IDirectoryOutboxSignal
{
    private SemaphoreSlim Semaphore { get; } = new(0, 1);

    /// <inheritdoc />
    public async ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await Semaphore.WaitAsync(timeout, cancellationToken);
    }

    /// <inheritdoc />
    public void Notify()
    {
        try
        {
            Semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending, which covers this notification.
        }
    }
}
