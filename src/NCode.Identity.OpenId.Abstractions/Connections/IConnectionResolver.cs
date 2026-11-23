// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Resolves the connections of the tenant of an <see cref="OpenIdContext"/> and selects the one a request signs in
/// through.
/// </summary>
// See ADR-1535.
[PublicAPI]
public interface IConnectionResolver
{
    /// <summary>
    /// Gets a connection of the tenant by its identifier, enabled or not.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> whose tenant owns the connection.</param>
    /// <param name="connectionId">The opaque identifier of the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the
    /// connection, or <see langword="null"/> when it does not exist or its strategy is not registered.</returns>
    ValueTask<ResolvedConnection?> GetByIdOrDefaultAsync(
        OpenIdContext openIdContext,
        string connectionId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets a connection of the tenant by its name, enabled or not.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> whose tenant owns the connection.</param>
    /// <param name="name">The name of the connection.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the
    /// connection, or <see langword="null"/> when it does not exist or its strategy is not registered.</returns>
    ValueTask<ResolvedConnection?> GetByNameOrDefaultAsync(
        OpenIdContext openIdContext,
        string name,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the connections a client may use: the enabled connections with a registered strategy that the client's
    /// <c>allowed_connections</c> setting allows, ordered by name.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> whose tenant owns the connections.</param>
    /// <param name="clientSettings">The effective settings of the client, or of the tenant when no client is known.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the
    /// allowed connections.</returns>
    ValueTask<IReadOnlyList<ResolvedConnection>> GetAllowedAsync(
        OpenIdContext openIdContext,
        IReadOnlySettingCollection clientSettings,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Selects the connection a request signs in through. A requested name must name an allowed connection, or the
    /// request is refused; without one, the client's <c>default_connection</c> is selected when it is usable, and
    /// otherwise no connection is selected.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> whose tenant owns the connections.</param>
    /// <param name="clientSettings">The effective settings of the client, or of the tenant when no client is known.</param>
    /// <param name="requestedName">The connection name the request asks for, if any.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing the
    /// selection.</returns>
    ValueTask<ConnectionSelection> SelectAsync(
        OpenIdContext openIdContext,
        IReadOnlySettingCollection clientSettings,
        string? requestedName,
        CancellationToken cancellationToken
    );
}
