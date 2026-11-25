// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Holds whether <see cref="DirectoryAnchorInitializer"/> has committed the platform's trust anchor, which the
/// directory's routes and readiness check wait for.
/// </summary>
internal sealed class DirectoryAnchorState
{
    private int initialized;

    /// <summary>
    /// Gets a value indicating whether the anchor cell's record and its root tenant's placement are committed.
    /// </summary>
    public bool IsInitialized => Volatile.Read(ref initialized) != 0;

    /// <summary>
    /// Records that the anchor is committed.
    /// </summary>
    public void MarkInitialized() => Volatile.Write(ref initialized, 1);
}
