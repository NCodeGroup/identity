// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// Checks a root key that an owner brings, as <see cref="IOwnerRootKeyManager.SetAsync"/> does before it probes the key
/// and as a tenant move does before it imports the owner's root key: every registered policy lets the owner bring a
/// root key, the key wrapper is allowed for an owner's root key and takes a key reference, the key reference fits the
/// wrapper and the cloud regions that the cell allows, and the access identity, when there is one, fits the wrapper.
/// </summary>
// See ADR-1414.
internal static class OwnerRootKeyRequestValidation
{
    /// <summary>
    /// Gets the reason that the owner cannot bring the root key that the request names.
    /// </summary>
    /// <param name="manager">The <see cref="IOwnerRootKeyManager"/> whose policies and key wrappers judge the
    /// key.</param>
    /// <param name="owner">The normalized <see cref="SecretOwner"/> that brings the key.</param>
    /// <param name="request">The <see cref="OwnerRootKeyRequest"/> that names the key.</param>
    /// <returns>The <see cref="OwnerRootKeyRefusal"/>, or <see langword="null"/> when the key passes.</returns>
    public static OwnerRootKeyRefusal? GetRefusalOrDefault(
        IOwnerRootKeyManager manager,
        SecretOwner owner,
        OwnerRootKeyRequest request
    )
    {
        if (!manager.CanBringRootKey(owner, out var reason))
            return new OwnerRootKeyRefusal(reason, IsOwnerRefused: true);

        if (
            !manager.TryValidateKeyReference(
                request.KeyWrapperName,
                request.KeyReference,
                out var failure
            )
        )
            return new OwnerRootKeyRefusal(failure, IsOwnerRefused: false);

        if (
            request.AccessIdentity is { } accessIdentity
            && !manager.TryValidateAccessIdentity(
                request.KeyWrapperName,
                accessIdentity,
                out failure
            )
        )
            return new OwnerRootKeyRefusal(failure, IsOwnerRefused: false);

        return null;
    }
}
