// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Authentication.Consent;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Settings;
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
    private const string NoConnectionAvailable = "No connection is available for the sign-in.";

    /// <summary>
    /// Chooses the connection the sign-in preselects from the offered ones.
    /// </summary>
    /// <param name="clientSettings">The client's effective settings.</param>
    /// <param name="requestedName">The connection name the request asks for, if any.</param>
    /// <param name="offered">The connections the sign-in offers.</param>
    /// <returns>The preselected connection, or <see langword="null"/> when none is.</returns>
    private static ResolvedConnection? Preselect(
        IReadOnlySettingCollection clientSettings,
        string? requestedName,
        IReadOnlyList<ResolvedConnection> offered
    )
    {
        if (requestedName is not null)
        {
            return offered.Count > 0 ? offered[0] : null;
        }

        var defaultName = clientSettings.GetValue(OpenIdSettingKeys.DefaultConnection);
        if (!string.IsNullOrEmpty(defaultName))
        {
            foreach (var connection in offered)
            {
                if (string.Equals(connection.Name, defaultName, StringComparison.OrdinalIgnoreCase))
                    return connection;
            }
        }

        return offered.Count == 1 ? offered[0] : null;
    }

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
        var offered = new List<ResolvedConnection>(allowed.Count);
        foreach (var connection in allowed)
        {
            if (
                requestedName is null
                || string.Equals(connection.Name, requestedName, StringComparison.OrdinalIgnoreCase)
            )
            {
                offered.Add(connection);
            }
        }

        var preselected = Preselect(clientSettings, requestedName, offered);

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

        if (string.IsNullOrEmpty(postedName))
        {
            if (connections.Preselected is not null)
                return new ConnectionSelection { Connection = connections.Preselected };

            return new ConnectionSelection
            {
                ErrorCode =
                    connections.Offered.Count > 1
                        ? ConnectionSelectionErrors.Required
                        : ConnectionSelectionErrors.Unavailable,
                ErrorDescription = NoConnectionAvailable,
            };
        }

        foreach (var connection in connections.Offered)
        {
            if (string.Equals(connection.Name, postedName, StringComparison.OrdinalIgnoreCase))
                return new ConnectionSelection { Connection = connection };
        }

        return new ConnectionSelection
        {
            ErrorCode = ConnectionSelectionErrors.Unavailable,
            ErrorDescription = NoConnectionAvailable,
        };
    }
}
