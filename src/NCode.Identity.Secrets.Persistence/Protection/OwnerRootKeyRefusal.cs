// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Secrets.Persistence.Protection;

/// <summary>
/// The reason that a root key an owner brings is refused, and whether a registered policy refuses the owner itself
/// rather than the key.
/// </summary>
/// <param name="Reason">The reason that the root key is refused.</param>
/// <param name="IsOwnerRefused">
/// <see langword="true"/> when a registered policy refuses to let the owner bring a root key, or
/// <see langword="false"/> when the key wrapper, the key reference or the access identity is refused.
/// </param>
// See ADR-1414.
internal sealed record OwnerRootKeyRefusal(string Reason, bool IsOwnerRefused);
