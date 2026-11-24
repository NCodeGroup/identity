// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Persistence.Stores;

/// <summary>
/// Provides an abstraction that wakes the publisher of the directory outbox after a unit of work that wrote outbox
/// entries has committed.
/// </summary>
[PublicAPI]
public interface IDirectoryOutboxNotifier
{
    /// <summary>
    /// Wakes the publisher.
    /// </summary>
    void Notify();
}
