// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Endpoints.Connections;

/// <summary>
/// Validates the intrinsic core preconditions for connection management operations: the name rule, a name unique
/// within the tenant, a registered strategy, the strategy's own validation of the options, and a display name that is
/// not empty, at most 300 characters and free of control characters. Register a custom implementation to replace or
/// extend the default validation.
/// </summary>
[PublicAPI]
public interface IConnectionValidator
{
    /// <summary>
    /// Validates that the specified connection may be created.
    /// </summary>
    /// <param name="connection">The <see cref="PersistedConnection"/> to be created.</param>
    /// <param name="storeManager">The <see cref="IStoreManager"/> for the current unit of work.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the first failed precondition, or <c>null</c> when valid.</returns>
    ValueTask<ManagementError?> ValidateCreateAsync(
        PersistedConnection connection,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Validates that the specified connection may be updated.
    /// </summary>
    /// <param name="connection">The current <see cref="PersistedConnection"/> being updated.</param>
    /// <param name="previousOptions">The options document the connection had before the update.</param>
    /// <param name="storeManager">The <see cref="IStoreManager"/> for the current unit of work.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ManagementError"/> describing the first failed precondition, or <c>null</c> when valid.</returns>
    ValueTask<ManagementError?> ValidateUpdateAsync(
        PersistedConnection connection,
        JsonElement previousOptions,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    );
}
