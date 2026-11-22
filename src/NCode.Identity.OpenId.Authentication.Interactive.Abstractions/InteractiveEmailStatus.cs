// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// Describes the email address of the signed-in end user and whether it is verified, as the email page shows it.
/// </summary>
/// <param name="email">The email address of the end user.</param>
/// <param name="isVerified">Whether the end user has proven they control the address.</param>
// See ADR-1533.
[PublicAPI]
public sealed class InteractiveEmailStatus(string email, bool isVerified)
{
    /// <summary>
    /// Gets the email address of the end user.
    /// </summary>
    public string Email { get; } = email;

    /// <summary>
    /// Gets a value indicating whether the end user has proven they control the address.
    /// </summary>
    public bool IsVerified { get; } = isVerified;
}
