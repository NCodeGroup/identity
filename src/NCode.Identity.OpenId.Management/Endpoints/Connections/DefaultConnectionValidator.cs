// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Persistence;
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
    /// <inheritdoc />
    public async ValueTask<ManagementError?> ValidateCreateAsync(
        PersistedConnection connection,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        if (!ConnectionNameRules.IsValid(connection.Name))
        {
            return BadRequest(
                $"The connection name must be 1 to {ConnectionNameRules.MaxLength} ASCII letters, digits, '-' or '_', starting with a letter or digit."
            );
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

        return ValidateContent(connection);
    }

    /// <inheritdoc />
    public ValueTask<ManagementError?> ValidateUpdateAsync(
        PersistedConnection connection,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var error = ValidateContent(connection);
        return ValueTask.FromResult(error);
    }

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

        var displayName = connection.DisplayName;
        if (
            string.IsNullOrWhiteSpace(displayName)
            || displayName.Length > OpenIdMaxLengths.DisplayName
            || displayName.Any(char.IsControl)
        )
        {
            return BadRequest(
                $"The display name must not be empty, longer than {OpenIdMaxLengths.DisplayName} characters or contain a control character."
            );
        }

        return null;
    }

    private static ManagementError BadRequest(string detail) =>
        new() { StatusCode = StatusCodes.Status400BadRequest, Detail = detail };
}
