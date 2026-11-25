// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Represents the directory's refusal of a cell's client authentication, because the directory has no registration for
/// the cell yet or holds other keys for it. The directory is reachable, so the cell keeps retrying until the platform's
/// operator registers it.
/// </summary>
[PublicAPI]
public sealed class DirectoryClientNotRegisteredException : DirectoryUnavailableException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryClientNotRegisteredException"/> class with the specified
    /// error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public DirectoryClientNotRegisteredException(string message)
        : base(message)
    {
        // nothing
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryClientNotRegisteredException"/> class with the specified
    /// error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public DirectoryClientNotRegisteredException(string message, Exception innerException)
        : base(message, innerException)
    {
        // nothing
    }
}
