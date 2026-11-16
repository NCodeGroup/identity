// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ResourceServers;

/// <summary>
/// Seeds the reserved directory (<see cref="GlobalDirectoryProtocol.DirectoryAudience"/>) and control-plane
/// (<see cref="GlobalDirectoryProtocol.ControlPlaneAudience"/>)
/// resource servers into the root tenant of the control-plane cell. Other cells request tokens for these audiences
/// with the scopes <see cref="GlobalDirectoryProtocol.DirectoryScope"/> and
/// <see cref="GlobalDirectoryProtocol.ControlPlaneScope"/>. The handler seeds nothing in a workload tenant or in a
/// cell that is not the control plane. It is idempotent: it creates each resource server only when it is absent.
/// </summary>
internal sealed class ControlPlaneResourceServerSeedHandler(
    ICryptoService cryptoService,
    IOptions<CellOptions> cellOptions
) : ICommandHandler<SeedTenantCommand>, ISupportMediatorPriority
{
    private ICryptoService CryptoService { get; } = cryptoService;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    public int MediatorPriority => DefaultMediatorPriorities.Low;

    /// <inheritdoc />
    public async ValueTask HandleAsync(
        SeedTenantCommand command,
        CancellationToken cancellationToken
    )
    {
        var context = command.Context;
        if (context.Plane != TenantPlane.Root || !CellOptions.IsControlPlane)
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

        await SeedAsync(
            store,
            context,
            GlobalDirectoryProtocol.ControlPlaneAudience,
            "Control plane",
            GlobalDirectoryProtocol.ControlPlaneScope,
            "Call the control plane.",
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
