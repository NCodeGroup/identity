// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Server;

/// <summary>
/// An offline command of the host, such as <c>setup</c> or <c>secret-protection</c>, which runs against the services of
/// a built host without starting it and ends with an exit code.
/// </summary>
[PublicAPI]
public interface IHostCommand
{
    /// <summary>
    /// Runs the command against the services of a built host, without starting the host.
    /// </summary>
    /// <param name="services">The services of the built host.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the command.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the command, containing its exit code.</returns>
    ValueTask<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken);
}
