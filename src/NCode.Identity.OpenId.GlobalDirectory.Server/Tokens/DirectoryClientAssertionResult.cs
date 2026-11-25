// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Tokens;

/// <summary>
/// The outcome of validating a client assertion.
/// </summary>
internal sealed class DirectoryClientAssertionResult
{
    private DirectoryClientAssertionResult() { }

    /// <summary>
    /// Gets whether the assertion is valid.
    /// </summary>
    [MemberNotNullWhen(true, nameof(CellName))]
    [MemberNotNullWhen(false, nameof(FailureReason))]
    public bool IsValid => CellName is not null;

    /// <summary>
    /// Gets the name of the authenticated cell of a valid assertion.
    /// </summary>
    public string? CellName { get; private init; }

    /// <summary>
    /// Gets the description of the refusal that the token endpoint gives the caller, which carries no exception text
    /// and no key error.
    /// </summary>
    public string? FailureReason { get; private init; }

    /// <summary>
    /// Creates the result of a valid assertion.
    /// </summary>
    /// <param name="cellName">The name of the authenticated cell.</param>
    public static DirectoryClientAssertionResult Success(string cellName) =>
        new() { CellName = cellName };

    /// <summary>
    /// Creates the result of a refused assertion.
    /// </summary>
    /// <param name="reason">The description of the refusal for the caller.</param>
    public static DirectoryClientAssertionResult Fail(string reason) =>
        new() { FailureReason = reason };
}
