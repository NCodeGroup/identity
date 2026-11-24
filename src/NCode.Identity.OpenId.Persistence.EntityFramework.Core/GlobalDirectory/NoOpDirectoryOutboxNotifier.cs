// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Provides the <see cref="IDirectoryOutboxNotifier"/> of a cell that has no publisher to wake.
/// </summary>
internal sealed class NoOpDirectoryOutboxNotifier : IDirectoryOutboxNotifier
{
    /// <inheritdoc />
    public void Notify() { }
}
