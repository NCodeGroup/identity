// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Represents the failure of a directory port to reach the global directory. A caller that needs the directory fails
/// closed on it and never treats an unreachable directory as an answer.
/// </summary>
[PublicAPI]
public sealed class DirectoryUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryUnavailableException"/> class with the specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public DirectoryUnavailableException(string message)
        : base(message)
    {
        // nothing
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryUnavailableException"/> class with the specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public DirectoryUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
        // nothing
    }
}
