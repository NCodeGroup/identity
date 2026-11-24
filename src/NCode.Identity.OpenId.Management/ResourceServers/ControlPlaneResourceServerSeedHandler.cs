// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Cryptography;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ResourceServers;

/// <summary>
/// Seeds the reserved directory (<see cref="GlobalDirectoryProtocol.DirectoryAudience"/>) resource server into the
/// root plane, which only the control-plane cell's root tenant is seeded in. Other cells request tokens for this
/// audience with the scope <see cref="GlobalDirectoryProtocol.DirectoryScope"/>. The handler seeds nothing in the
/// workload plane. It is idempotent: it creates the resource server only when it is absent.
/// </summary>
internal sealed class ControlPlaneResourceServerSeedHandler(ICryptoService cryptoService)
    : ICommandHandler<SeedTenantCommand>,
        ISupportMediatorPriority
{
    private ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public int MediatorPriority => DefaultMediatorPriorities.Low;

    /// <inheritdoc />
    public async ValueTask HandleAsync(
        SeedTenantCommand command,
        CancellationToken cancellationToken
    )
    {
        var context = command.Context;
        if (context.Plane != TenantPlane.Root)
        {
            return;
        }

        var store = context.StoreManager.GetStore<IResourceServerStore>();

        await SeedAsync(
            store,
            context,
            GlobalDirectoryProtocol.DirectoryAudience,
            "Directory",
            GlobalDirectoryProtocol.DirectoryScope,
            "Read and write the global directory.",
            cancellationToken
        );
    }

    private async ValueTask SeedAsync(
        IResourceServerStore store,
        TenantSeedContext context,
        string identifier,
        string name,
        string scope,
        string scopeDescription,
        CancellationToken cancellationToken
    )
    {
        var existing = await store.GetByIdentifierOrDefaultAsync(identifier, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var resourceServerId = CryptoService.GenerateResourceId();

        var resourceServer = new PersistedResourceServer
        {
            TenantId = context.TenantId,
            ResourceServerId = resourceServerId,
            Identifier = identifier,
            ConcurrencyToken = string.Empty,
            Name = name,
            IsSystem = true,
            IsDisabled = false,
            Settings = new PersistedResourceServerSettings
            {
                TenantId = context.TenantId,
                ResourceServerId = resourceServerId,
                Value = JsonElements.EmptyObject,
            },
            Scopes =
            [
                new PersistedScope
                {
                    Value = scope,
                    Description = scopeDescription,
                    IsSystem = true,
                },
            ],
            Secrets = new PersistedResourceServerSecrets
            {
                TenantId = context.TenantId,
                ResourceServerId = resourceServerId,
                Value = [],
            },
        };

        await store.AddAsync(resourceServer, cancellationToken);
    }
}
