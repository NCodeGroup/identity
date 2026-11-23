// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// Describes the outcome of an operation on a one-time email code: a code is sent, a code is verified, or the operation
/// is refused with the error code the page shows.
/// </summary>
// See ADR-1533.
[PublicAPI]
public sealed class InteractiveEmailCodeResult
{
    /// <summary>
    /// Gets the result for a code that is verified.
    /// </summary>
    public static InteractiveEmailCodeResult Verified { get; } = new(null, null);

    /// <summary>
    /// Gets the result for a backend that has no email code to send or verify for the end user.
    /// </summary>
    public static InteractiveEmailCodeResult NotSupported { get; } =
        new(null, InteractiveEmailCodeErrors.NotSupported);

    /// <summary>
    /// Creates the result for a code that is sent.
    /// </summary>
    /// <param name="flowId">The handle of the flow that the page presents back with the code.</param>
    /// <returns>The <see cref="InteractiveEmailCodeResult"/> that carries the handle.</returns>
    public static InteractiveEmailCodeResult Sent(string flowId)
    {
        ArgumentException.ThrowIfNullOrEmpty(flowId);
        return new InteractiveEmailCodeResult(flowId, null);
    }

    /// <summary>
    /// Creates the result for an operation that the backend refuses, so nothing is sent or verified.
    /// </summary>
    /// <param name="error">
    /// The code that names the reason, one of the <see cref="InteractiveEmailCodeErrors"/> or a code of the backend.
    /// </param>
    /// <returns>The <see cref="InteractiveEmailCodeResult"/> that is refused with the error code.</returns>
    public static InteractiveEmailCodeResult Refused(string error)
    {
        ArgumentException.ThrowIfNullOrEmpty(error);
        return new InteractiveEmailCodeResult(null, error);
    }

    private InteractiveEmailCodeResult(string? flowId, string? error)
    {
        FlowId = flowId;
        Error = error;
    }

    /// <summary>
    /// Gets the handle of the flow a sent code belongs to, or <see langword="null"/> when no code is sent.
    /// </summary>
    public string? FlowId { get; }

    /// <summary>
    /// Gets the code that names the reason the operation is refused, or <see langword="null"/> when it is accepted.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Gets a value indicating whether the operation is accepted.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsAccepted => Error is null;
}
