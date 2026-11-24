// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Endpoints.Connections;

/// <summary>
/// Provides the default implementation of <see cref="IConnectionValidator"/>.
/// </summary>
internal sealed class DefaultConnectionValidator(IConnectionStrategyCatalog strategyCatalog)
    : IConnectionValidator
{
    /// <summary>
    /// Creates the <c>400 Bad Request</c> error with the specified detail.
    /// </summary>
    /// <param name="detail">The explanation the caller reads.</param>
    /// <returns>The error to return to the caller.</returns>
    private static ManagementError BadRequest(string detail) =>
        new() { StatusCode = StatusCodes.Status400BadRequest, Detail = detail };

    /// <inheritdoc />
    public async ValueTask<ManagementError?> ValidateCreateAsync(
        PersistedConnection connection,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var nameError = ConnectionContentRules.GetNameErrorOrDefault(connection.Name);
        if (nameError is not null)
        {
            return BadRequest(nameError);
        }

        var contentError = ValidateContent(connection);
        if (contentError is not null)
        {
            return contentError;
        }

        var store = storeManager.GetStore<IConnectionStore>();
        var existing = await store.GetByNameOrDefaultAsync(connection.Name, cancellationToken);
        if (existing is not null)
        {
            return new ManagementError
            {
                StatusCode = StatusCodes.Status409Conflict,
                Detail = "A connection with the specified name already exists.",
            };
        }

        return null;
    }

    /// <inheritdoc />
    public ValueTask<ManagementError?> ValidateUpdateAsync(
        PersistedConnection connection,
        JsonElement previousOptions,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var result = ValidateUpdate(connection, previousOptions);
        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Validates an updated connection's content and the change from its previous options.
    /// </summary>
    /// <param name="connection">The connection with its updated values.</param>
    /// <param name="previousOptions">The options the connection had before the update.</param>
    /// <returns>The first error found, or <see langword="null"/> when the update is valid.</returns>
    private ManagementError? ValidateUpdate(
        PersistedConnection connection,
        JsonElement previousOptions
    )
    {
        var error = ValidateContent(connection);
        if (error is not null)
        {
            return error;
        }

        if (!strategyCatalog.TryGetStrategy(connection.Strategy, out var strategy))
        {
            return BadRequest("The connection strategy is not registered.");
        }

        var changeError = strategy.ValidateOptionsChange(previousOptions, connection.Options);
        return changeError is null ? null : BadRequest(changeError);
    }

    /// <summary>
    /// Validates the strategy, options and display name of a connection.
    /// </summary>
    /// <param name="connection">The connection to validate.</param>
    /// <returns>The first error found, or <see langword="null"/> when the content is valid.</returns>
    private ManagementError? ValidateContent(PersistedConnection connection)
    {
        if (!strategyCatalog.TryGetStrategy(connection.Strategy, out var strategy))
        {
            return BadRequest("The connection strategy is not registered.");
        }

        var optionsError = strategy.ValidateOptions(connection.Options);
        if (optionsError is not null)
        {
            return BadRequest(optionsError);
        }

        var displayNameError = ConnectionContentRules.GetDisplayNameErrorOrDefault(
            connection.DisplayName
        );
        return displayNameError is null ? null : BadRequest(displayNameError);
    }
}
