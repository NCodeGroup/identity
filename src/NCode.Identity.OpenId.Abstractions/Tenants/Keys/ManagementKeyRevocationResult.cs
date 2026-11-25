// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Tenants.Keys;

/// <summary>
/// The outcome of revoking a management key: the control-plane tenant holds no such key, or the key is revoked and a
/// replacement signs.
/// </summary>
// See ADR-1536.
[PublicAPI]
public sealed class ManagementKeyRevocationResult
{
    private ManagementKeyRevocationResult(bool isRevoked, string? replacementKeyId)
    {
        IsRevoked = isRevoked;
        ReplacementKeyId = replacementKeyId;
    }

    /// <summary>
    /// Gets the outcome of a key identifier that names no key of the control-plane tenant.
    /// </summary>
    public static ManagementKeyRevocationResult NotFound { get; } = new(false, null);

    /// <summary>
    /// Gets a value indicating whether the key was revoked.
    /// </summary>
    public bool IsRevoked { get; }

    /// <summary>
    /// Gets the key identifier of the key that signs in place of the revoked one, or <c>null</c> when no key was
    /// revoked.
    /// </summary>
    public string? ReplacementKeyId { get; }

    /// <summary>
    /// Creates the outcome of a revoked key.
    /// </summary>
    /// <param name="replacementKeyId">The key identifier of the key that signs in place of the revoked one.</param>
    /// <returns>The outcome.</returns>
    public static ManagementKeyRevocationResult Revoked(string replacementKeyId)
    {
        ArgumentNullException.ThrowIfNull(replacementKeyId);
        return new ManagementKeyRevocationResult(true, replacementKeyId);
    }
}
