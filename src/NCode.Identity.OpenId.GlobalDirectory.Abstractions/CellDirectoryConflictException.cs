// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Represents the refusal of a cell write by the global directory, such as a cell that reports the management URL of
/// another cell. The message is the explanation for the caller.
/// </summary>
[PublicAPI]
public sealed class CellDirectoryConflictException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CellDirectoryConflictException"/> class.
    /// </summary>
    public CellDirectoryConflictException()
    {
        // nothing
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CellDirectoryConflictException"/> class with the specified error
    /// message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public CellDirectoryConflictException(string message)
        : base(message)
    {
        // nothing
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CellDirectoryConflictException"/> class with the specified error
    /// message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public CellDirectoryConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
        // nothing
    }
}
