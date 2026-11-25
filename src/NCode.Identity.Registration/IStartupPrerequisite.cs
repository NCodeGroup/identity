// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Registration;

/// <summary>
/// A step that every use of a built host's services needs before it works, such as loading the key rings that sign a
/// cell's client assertions. The host runs it as it starts, before any hosted service, and an offline command of the
/// host, which never starts the host, runs every registered prerequisite, in registration order, before its own work,
/// so a command works with the same services the running host does. A prerequisite runs its work once per process, so
/// running it again does nothing.
/// </summary>
// See ADR-1536.
[PublicAPI]
public interface IStartupPrerequisite
{
    /// <summary>
    /// Runs the step, or does nothing when it has already run in this process.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask PrepareAsync(CancellationToken cancellationToken);
}
