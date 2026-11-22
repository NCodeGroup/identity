// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Cryptography;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ResourceServers;

/// <summary>
/// Seeds the reserved management resource server (<c>urn:ncode:management</c>) into a tenant. Its tenant-plane scope
/// families (a tenant's own settings and secrets, plus its clients, client secrets, resource servers, client grants,
/// and grants) are seeded into every tenant; its control-plane families (platform settings, team management and
/// tenant provisioning) are added only when seeding the root tenant. The handler is idempotent: it creates the
/// resource server only when it is absent.
/// </summary>
internal sealed class ManagementResourceServerSeedHandler(ICryptoService cryptoService)
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
        var store = context.StoreManager.GetStore<IResourceServerStore>();

        var existing = await store.GetByIdentifierOrDefaultAsync(
            OpenIdConstants.SystemResourceServerIdentifiers.Management,
            cancellationToken
        );
        if (existing is not null)
        {
            return;
        }

        var resourceServerId = CryptoService.GenerateResourceId();

        var resourceServer = new PersistedResourceServer
        {
            TenantId = context.TenantId,
            ResourceServerId = resourceServerId,
            Identifier = OpenIdConstants.SystemResourceServerIdentifiers.Management,
            ConcurrencyToken = string.Empty,
            Name = "Management",
            IsSystem = true,
            IsDisabled = false,
            Settings = new PersistedResourceServerSettings
            {
                TenantId = context.TenantId,
                ResourceServerId = resourceServerId,
                Value = JsonElements.EmptyObject,
            },
            Scopes = BuildScopes(context.Plane),
            Secrets = new PersistedResourceServerSecrets
            {
                TenantId = context.TenantId,
                ResourceServerId = resourceServerId,
                Value = [],
            },
        };

        await store.AddAsync(resourceServer, cancellationToken);
    }

    private static List<PersistedScope> BuildScopes(TenantPlane plane)
    {
        var readGrantsValue = ManagementScopes.For(
            ManagementScopes.Verbs.Read,
            ManagementScopes.Families.Grants
        );
        var revokeGrantsValue = ManagementScopes.For(
            ManagementScopes.Verbs.Delete,
            ManagementScopes.Families.Grants
        );
        IEnumerable<PersistedScope> scopes =
        [
            // Tenant-plane families, seeded into every tenant.
            .. SettingsScopes(ManagementScopes.Families.TenantSettings, "tenant settings"),
            .. CrudScopes(ManagementScopes.Families.TenantSecrets, "tenant secrets"),
            .. SettingsScopes(ManagementScopes.Families.EncryptionKeys, "encryption keys"),
            .. CrudScopes(ManagementScopes.Families.Clients, "clients"),
            .. CrudScopes(ManagementScopes.Families.ClientSecrets, "client secrets"),
            .. CrudScopes(ManagementScopes.Families.ResourceServers, "resource servers"),
            .. CrudScopes(
                ManagementScopes.Families.ResourceServerSecrets,
                "resource server secrets"
            ),
            .. CrudScopes(ManagementScopes.Families.ClientGrants, "client grants"),
            .. CrudScopes(ManagementScopes.Families.LocalAccounts, "local accounts"),
            Scope(readGrantsValue, "Read grants (user authorizations)."),
            Scope(revokeGrantsValue, "Revoke grants (user authorizations)."),
        ];

        if (plane == TenantPlane.Root)
        {
            // Control-plane families, seeded only into the root tenant.
            IEnumerable<PersistedScope> rootScopes =
            [
                .. SettingsScopes(ManagementScopes.Families.PlatformSettings, "platform settings"),
                .. SettingsScopes(
                    ManagementScopes.Families.PlatformEncryptionKeys,
                    "platform encryption keys"
                ),
                .. CrudScopes(ManagementScopes.Families.Teams, "teams"),
                .. SettingsScopes(ManagementScopes.Families.TeamSettings, "team settings"),
                .. CrudScopes(ManagementScopes.Families.Tenants, "tenants"),
            ];
            scopes = scopes.Concat(rootScopes);
        }

        return scopes
            .GroupBy(scope => scope.Value, StringComparer.Ordinal)
            .Select(byValue => byValue.First())
            .ToList();
    }

    private static IEnumerable<PersistedScope> CrudScopes(string family, string noun)
    {
        var readValue = ManagementScopes.For(ManagementScopes.Verbs.Read, family);
        var createValue = ManagementScopes.For(ManagementScopes.Verbs.Create, family);
        var updateValue = ManagementScopes.For(ManagementScopes.Verbs.Update, family);
        var deleteValue = ManagementScopes.For(ManagementScopes.Verbs.Delete, family);
        return
        [
            Scope(readValue, $"Read {noun}."),
            Scope(createValue, $"Create {noun}."),
            Scope(updateValue, $"Update {noun}."),
            Scope(deleteValue, $"Delete {noun}."),
        ];
    }

    private static IEnumerable<PersistedScope> SettingsScopes(string family, string noun)
    {
        var readValue = ManagementScopes.For(ManagementScopes.Verbs.Read, family);
        var updateValue = ManagementScopes.For(ManagementScopes.Verbs.Update, family);
        return [Scope(readValue, $"Read {noun}."), Scope(updateValue, $"Update {noun}.")];
    }

    private static PersistedScope Scope(string value, string description) =>
        new()
        {
            Value = value,
            Description = description,
            IsSystem = true,
        };
}
