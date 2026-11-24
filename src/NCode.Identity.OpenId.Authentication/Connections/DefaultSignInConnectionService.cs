// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Authentication.Consent;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Authentication.Connections;

/// <summary>
/// Provides a default implementation of <see cref="ISignInConnectionService"/> that offers the client's allowed
/// connections whose strategy verifies a password.
/// </summary>
// See ADR-1535.
internal class DefaultSignInConnectionService(
    PendingAuthorizationRequestReader requestReader,
    IOpenIdClientResolver clientResolver,
    IConnectionResolver connectionResolver
) : ISignInConnectionService
{
    private PendingAuthorizationRequestReader RequestReader { get; } = requestReader;
    private IOpenIdClientResolver ClientResolver { get; } = clientResolver;
    private IConnectionResolver ConnectionResolver { get; } = connectionResolver;

    /// <inheritdoc />
    public async ValueTask<SignInConnections> GetOfferedAsync(
        OpenIdContext openIdContext,
        string? returnUrl,
        CancellationToken cancellationToken
    )
    {
        var request = string.IsNullOrEmpty(returnUrl)
            ? null
            : await RequestReader.ReadAsync(openIdContext, returnUrl, cancellationToken);

        IReadOnlySettingCollection clientSettings = openIdContext
            .Tenant
            .SettingsProvider
            .Collection;
        if (request is not null)
        {
            var client = await ClientResolver.ResolveClientOrDefaultAsync(
                openIdContext,
                request.ClientId,
                cancellationToken
            );
            if (client is not null)
            {
                clientSettings = client.Settings;
            }
        }

        var requestedName = request is null
            ? null
            : ConnectionRequests.GetRequestedName(request.Connection, request.AcrValues);

        var allowed = await ConnectionResolver.GetAllowedAsync(
            openIdContext,
            clientSettings,
            ConnectionSignInMethods.Password,
            cancellationToken
        );
        IReadOnlyList<ResolvedConnection> offered = allowed
            .Where(connection =>
                requestedName is null
                || string.Equals(connection.Name, requestedName, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        var preselected = await PreselectAsync(
            openIdContext,
            clientSettings,
            requestedName,
            offered,
            cancellationToken
        );

        return new SignInConnections { Offered = offered, Preselected = preselected };
    }

    /// <inheritdoc />
    public async ValueTask<ConnectionSelection> SelectAsync(
        OpenIdContext openIdContext,
        string? returnUrl,
        string? postedName,
        CancellationToken cancellationToken
    )
    {
        var connections = await GetOfferedAsync(openIdContext, returnUrl, cancellationToken);

        var selected = string.IsNullOrEmpty(postedName)
            ? connections.Preselected
            : connections.Offered.FirstOrDefault(connection =>
                string.Equals(connection.Name, postedName, StringComparison.OrdinalIgnoreCase)
            );

        return selected is null
            ? new ConnectionSelection
            {
                ErrorDescription = "No connection is available for the sign-in.",
            }
            : new ConnectionSelection { Connection = selected };
    }

    /// <summary>
    /// Chooses the connection the sign-in preselects from the offered ones.
    /// </summary>
    /// <param name="openIdContext">The context of the request.</param>
    /// <param name="clientSettings">The client's effective settings.</param>
    /// <param name="requestedName">The connection name the request asks for, if any.</param>
    /// <param name="offered">The connections the sign-in offers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The preselected connection, or <see langword="null"/> when none is.</returns>
    private async ValueTask<ResolvedConnection?> PreselectAsync(
        OpenIdContext openIdContext,
        IReadOnlySettingCollection clientSettings,
        string? requestedName,
        IReadOnlyList<ResolvedConnection> offered,
        CancellationToken cancellationToken
    )
    {
        if (requestedName is not null)
        {
            return offered.Count > 0 ? offered[0] : null;
        }

        var selection = await ConnectionResolver.SelectAsync(
            openIdContext,
            clientSettings,
            requestedName: null,
            cancellationToken
        );
        var defaultConnection = selection.Connection;
        if (defaultConnection is not null)
        {
            var match = offered.FirstOrDefault(connection =>
                string.Equals(
                    connection.ConnectionId,
                    defaultConnection.ConnectionId,
                    StringComparison.Ordinal
                )
            );
            if (match is not null)
            {
                return match;
            }
        }

        return offered.Count == 1 ? offered[0] : null;
    }
}
