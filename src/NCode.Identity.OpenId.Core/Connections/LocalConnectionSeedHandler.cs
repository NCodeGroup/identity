// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.Cryptography;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Connections;

/// <summary>
/// Seeds the <c>local</c> connection, a username-and-password connection over the default local-account store, into
/// every tenant. The handler is idempotent: it creates the connection only when it is absent (ADR-1535).
/// </summary>
internal sealed class LocalConnectionSeedHandler(ICryptoService cryptoService)
    : ICommandHandler<SeedTenantCommand>,
        ISupportMediatorPriority
{
    private ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public int MediatorPriority => DefaultMediatorPriorities.High;

    /// <inheritdoc />
    public async ValueTask HandleAsync(
        SeedTenantCommand command,
        CancellationToken cancellationToken
    )
    {
        var context = command.Context;
        var store = context.StoreManager.GetStore<IConnectionStore>();

        var existing = await store.GetByNameOrDefaultAsync(
            ConnectionDefaults.Name,
            cancellationToken
        );
        if (existing is not null)
        {
            return;
        }

        var connectionId = CryptoService.GenerateResourceId();

        var options = new LocalConnectionOptions { Store = LocalAccountStoreNames.Default };

        var connection = new PersistedConnection
        {
            TenantId = context.TenantId,
            ConnectionId = connectionId,
            Name = ConnectionDefaults.Name,
            Strategy = ConnectionStrategyNames.Local,
            DisplayName = "Username and password",
            IsEnabled = true,
            Options = JsonSerializer.SerializeToElement(options),
            Settings = new PersistedConnectionSettings
            {
                TenantId = context.TenantId,
                ConnectionId = connectionId,
                Value = JsonElements.EmptyObject,
            },
        };

        await store.AddAsync(connection, cancellationToken);
    }
}
