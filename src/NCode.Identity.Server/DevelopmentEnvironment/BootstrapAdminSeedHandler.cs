// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.Json;
using NCode.Identity.OpenId;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Management;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.Server.Logging;
using NCode.Mediator;

namespace NCode.Identity.Server.DevelopmentEnvironment;

/// <summary>
/// Seeds the configured bootstrap administrator client into the configured bootstrap tenant as part of the tenant seed
/// fan-out. The client registers <c>client_secret_basic</c>, so it presents the configured secret with HTTP Basic
/// authentication. The client holds a persisted <c>GlobalAdmin</c> role assignment at the platform node, which the
/// control plane answers for the client's principal, so an operator can make the first authorized management call. The
/// handler is idempotent and inert unless a bootstrap client id and secret are configured, and it seeds exactly the one
/// tenant <see cref="BootstrapAdminOptions.TenantId"/> names — never every tenant, which would hand a GlobalAdmin
/// credential to each tenant created at runtime. The client is checked before it is stored, so a ceiling that excludes
/// <c>client_secret_basic</c> stops the seed. An existing client is left as it is, apart from its secret and its
/// platform assignment; the reconciliation of the platform's settings, team by team, reports it, as it reports every
/// stored client, when the settings make it incoherent.
/// </summary>
// See ADR-1044 (the bootstrap administrator), ADR-1166 (a seeder that names a tenant seeds exactly that tenant), and
// ADR-1412 (an existing client is reported by the per-team reconciliation).
internal sealed class BootstrapAdminSeedHandler(
    IOptions<BootstrapAdminOptions> bootstrapOptionsAccessor,
    IClientSecretIssuer clientSecretIssuer,
    ISecretProtectorProvider secretProtectorProvider,
    ICryptoService cryptoService,
    TimeProvider timeProvider,
    SeededClientValidator seededClientValidator,
    ILogger<BootstrapAdminSeedHandler> logger
) : ICommandHandler<SeedTenantCommand>, ISupportMediatorPriority
{
    // The bootstrap credential is a shared secret, which RFC 6749 §2.3.1 has the client present with HTTP Basic.
    private const string SeededAuthenticationMethod = OpenIdProtocol
        .ClientAuthenticationMethods
        .ClientSecretBasic;

    private BootstrapAdminOptions Options { get; } = bootstrapOptionsAccessor.Value;
    private IClientSecretIssuer ClientSecretIssuer { get; } = clientSecretIssuer;
    private ISecretProtectorProvider SecretProtectorProvider { get; } = secretProtectorProvider;
    private ICryptoService CryptoService { get; } = cryptoService;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private SeededClientValidator SeededClientValidator { get; } = seededClientValidator;
    private ILogger<BootstrapAdminSeedHandler> Logger { get; } = logger;

    /// <inheritdoc />
    public int MediatorPriority => DefaultMediatorPriorities.Low;

    /// <inheritdoc />
    public async ValueTask HandleAsync(
        SeedTenantCommand command,
        CancellationToken cancellationToken
    )
    {
        var clientId = Options.ClientId;
        var clientSecret = Options.ClientSecret;
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            return;
        }

        var context = command.Context;
        if (!TargetsTenant(context))
        {
            return;
        }

        var tenantId = context.TenantId;
        var store = context.StoreManager.GetStore<IClientStore>();

        // The configured secret is supplied Base64Url-encoded (not plaintext) so a raw process-memory snapshot does
        // not expose it directly; decode it straight into a pinned, zeroed-on-return buffer threaded as ReadOnlyMemory.
        using var secretOwner = StrictBase64Url.RentDecoded(
            clientSecret,
            isSensitive: true,
            out ReadOnlyMemory<byte> clientSecretBytes
        );

        var existing = await store.GetOrDefaultAsync(clientId, cancellationToken);
        if (existing is not null)
        {
            // The configured secret is authoritative. It is stored again under the tenant's current master key, so a
            // stale or undecryptable stored secret is healed instead of leaving the bootstrap client permanently unable
            // to authenticate. Its form follows the method the client is registered for, which an operator may set to
            // another method than the seeded one (ADR-1391).
            var tokenEndpointAuthMethod = await ClientSecretIssuer.GetTokenEndpointAuthMethodAsync(
                context.Tenant.TeamId,
                tenantId,
                context.Tenant.Settings.Value,
                existing.ClientId,
                existing.Settings.Value,
                cancellationToken
            );
            var refreshProtector = await GetProtectorAsync(tenantId, cancellationToken);
            await RefreshClientSecretAsync(
                store,
                refreshProtector,
                clientId,
                clientSecretBytes,
                tokenEndpointAuthMethod,
                cancellationToken
            );
            Logger.BootstrapAdminClientSecretRefreshed(clientId, tenantId);
            var firstRoleAssignmentStore = context.StoreManager.GetStore<IRoleAssignmentStore>();
            await EnsureGlobalAdminAsync(
                firstRoleAssignmentStore,
                tenantId,
                existing.PrincipalId,
                cancellationToken
            );
            return;
        }

        var settings = CreateSettings();

        // See ADR-1206.
        await SeededClientValidator.EnsureCanBeSeededAsync(
            "bootstrap administrator client",
            clientId,
            context.Tenant,
            settings,
            cancellationToken
        );

        // The tenant's master key is loaded only once the client is accepted, so a refused seed never creates it.
        var protector = await GetProtectorAsync(tenantId, cancellationToken);
        var persistedClient = CreateClient(
            tenantId,
            clientId,
            settings,
            clientSecretBytes,
            protector
        );

        await store.AddAsync(persistedClient, cancellationToken);

        // The store assigns the client's principal identifier when it adds the client.
        var roleAssignmentStore = context.StoreManager.GetStore<IRoleAssignmentStore>();
        await EnsureGlobalAdminAsync(
            roleAssignmentStore,
            tenantId,
            persistedClient.PrincipalId,
            cancellationToken
        );

        Logger.BootstrapAdminClientSeeded(clientId, tenantId);
    }

    // The role that confers every management authority (ADR-1417) is held at the platform node, in the control-plane
    // tenant, as a persisted assignment; it is added only when the client does not already hold it. The seed runs in
    // the tenant's scope, so the assignments at the platform node are read there.
    private async ValueTask EnsureGlobalAdminAsync(
        IRoleAssignmentStore store,
        string tenantId,
        string principalId,
        CancellationToken cancellationToken
    )
    {
        var assignments = await store.GetByResourceAsync(
            ResourceNodeTypes.Platform,
            OpenIdConstants.PlatformResourceId,
            cancellationToken
        );
        var isAssigned = assignments.Any(assignment =>
            string.Equals(assignment.PrincipalId, principalId, StringComparison.Ordinal)
            && string.Equals(
                assignment.RoleName,
                BuiltInRoles.GlobalAdmin,
                StringComparison.Ordinal
            )
            && string.Equals(
                assignment.ResourceType,
                ResourceNodeTypes.Platform,
                StringComparison.Ordinal
            )
            && string.Equals(
                assignment.ResourceId,
                OpenIdConstants.PlatformResourceId,
                StringComparison.Ordinal
            )
        );
        if (isAssigned)
        {
            return;
        }

        var assignment = new PersistedRoleAssignment
        {
            TenantId = tenantId,
            AssignmentId = CryptoService.GenerateResourceId(),
            PrincipalId = principalId,
            RoleName = BuiltInRoles.GlobalAdmin,
            ResourceType = ResourceNodeTypes.Platform,
            ResourceId = OpenIdConstants.PlatformResourceId,
            ConcurrencyToken = string.Empty,
        };
        await store.AddAsync(assignment, cancellationToken);
    }

    private ValueTask<ISecretProtector> GetProtectorAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var secretOwner = SecretOwner.ForTenant(tenantId);
        return SecretProtectorProvider.GetProtectorAsync(secretOwner, [], cancellationToken);
    }

    private async ValueTask RefreshClientSecretAsync(
        IClientStore store,
        ISecretProtector protector,
        string clientId,
        ReadOnlyMemory<byte> clientSecret,
        string? tokenEndpointAuthMethod,
        CancellationToken cancellationToken
    )
    {
        var existingSecrets = await store.GetSecretsOrDefaultAsync(clientId, cancellationToken);
        if (existingSecrets is not null)
        {
            foreach (var secret in existingSecrets.Value)
            {
                await store.RemoveSecretAsync(clientId, secret.SecretId, cancellationToken);
            }
        }

        var persistedSecret = CreatePersistedSecret(
            protector,
            clientSecret,
            tokenEndpointAuthMethod
        );
        await store.AddSecretAsync(clientId, persistedSecret, cancellationToken);
    }

    private bool TargetsTenant(TenantSeedContext context) =>
        // BootstrapAdminOptionsValidator requires the tenant whenever the bootstrap administrator is configured, and it
        // must be the control-plane tenant, where the platform role assignment is read.
        string.Equals(context.TenantId, Options.TenantId, StringComparison.Ordinal);

    private static JsonElement CreateSettings()
    {
        var settings = new Dictionary<string, object>
        {
            [OpenIdSettingNames.TokenEndpointAuthMethod] = SeededAuthenticationMethod,
        };
        return JsonSerializer.SerializeToElement(settings);
    }

    private PersistedClient CreateClient(
        string tenantId,
        string clientId,
        JsonElement settings,
        ReadOnlyMemory<byte> clientSecret,
        ISecretProtector protector
    )
    {
        var persistedSecret = CreatePersistedSecret(
            protector,
            clientSecret,
            SeededAuthenticationMethod
        );

        return new PersistedClient
        {
            TenantId = tenantId,
            ClientId = clientId,
            IsDisabled = false,
            Settings = new PersistedClientSettings
            {
                TenantId = tenantId,
                ClientId = clientId,
                Value = settings,
            },
            Secrets = new PersistedClientSecrets
            {
                TenantId = tenantId,
                ClientId = clientId,
                Value = [persistedSecret],
            },
        };
    }

    private PersistedSecret CreatePersistedSecret(
        ISecretProtector protector,
        ReadOnlyMemory<byte> clientSecret,
        string? tokenEndpointAuthMethod
    )
    {
        var now = TimeProvider.GetUtcNow();

        // Persist the configured client_secret through the client-secret issuer (ADR-1391), in the form the client's
        // method needs: a PBKDF2 verifier when the method only compares it, reversible when it keys from it.
        var request = new SharedSecretRequest
        {
            // An opaque, unique id per secret (the repo-wide CSPRNG resource-id scheme) so a refresh (remove-then-add
            // in one unit of work) never collides with the outgoing secret's id, and the id reveals nothing.
            SecretId = CryptoService.GenerateResourceId(),
            CreatedWhen = now,
            ExpiresWhen = now.AddYears(100),
        };

        return ClientSecretIssuer.ImportClientSecret(
            protector,
            tokenEndpointAuthMethod,
            request,
            clientSecret.Span
        );
    }
}
