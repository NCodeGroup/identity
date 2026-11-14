// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Bff.Sessions;

/// <summary>
/// Protects the members of the <see cref="BffStoreKeys.ClientsIndex"/> index, so that the shared state never holds a
/// client's frontend, authority or client identifier in the clear.
/// </summary>
// See ADR-1443 and ADR-1018.
internal interface IBffClientMemberProtector
{
    /// <summary>
    /// Gets the member of <see cref="BffStoreKeys.ClientsIndex"/> that lists a client for the UTC day of a session's
    /// absolute expiry.
    /// </summary>
    /// <remarks>
    /// The same client and UTC day give the same member on one replica, so the index holds one member per replica,
    /// client and day, and each member expires at the end of its day (<see cref="BffStoreKeys.ClientMemberExpiresAt"/>),
    /// so adding a session never shortens a client's listing.
    /// </remarks>
    /// <param name="client">The client that the session was issued to.</param>
    /// <param name="absoluteExpiresAt">The absolute expiry of the session.</param>
    /// <returns>The protected member.</returns>
    string Protect(BffSessionClient client, DateTimeOffset absoluteExpiresAt);

    /// <summary>
    /// Unprotects a member of <see cref="BffStoreKeys.ClientsIndex"/> into its client.
    /// </summary>
    /// <param name="member">The protected member.</param>
    /// <param name="client">The client that the member lists.</param>
    /// <returns><see langword="true"/> when the member is readable; otherwise, <see langword="false"/>.</returns>
    bool TryUnprotect(string member, out BffSessionClient client);
}
