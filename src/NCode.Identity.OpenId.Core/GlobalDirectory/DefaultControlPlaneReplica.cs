// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.DataContracts;

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the default implementation of <see cref="IControlPlaneReplica"/>. It reads the replicated documents and
/// the refused management keys from the cell's own database and never calls the directory or another cell. A team's
/// authority document is read under the lower-cased team identifier, for the team and every team above it in the cell's
/// mirror of the teams.
/// </summary>
// See ADR-1536.
internal sealed class DefaultControlPlaneReplica(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    ISecretSerializer secretSerializer,
    TimeProvider timeProvider
) : IControlPlaneReplica
{
    /// <summary>
    /// The reason that the replica gives when it opens an unscoped ambient tenant scope to read the replicas.
    /// </summary>
    internal const string UnscopedReason = "control-plane replica";

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private ISecretSerializer SecretSerializer { get; } = secretSerializer;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    public async ValueTask<ReplicatedManagementKeys> GetManagementKeysAsync(
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IReplicatedDocumentStore>();

        var document = await store.GetOrDefaultAsync(
            ReplicatedDocumentKinds.ManagementKeys,
            string.Empty,
            cancellationToken
        );
        var revokedKeys = await store.ListRevokedKeysAsync(cancellationToken);
        var revokedKeyIds = revokedKeys
            .Select(revokedKey => revokedKey.KeyId)
            .ToHashSet(StringComparer.Ordinal);

        var content = Deserialize<ManagementKeysDocumentContent>(document);
        if (content is null || content.Keys.Count == 0)
        {
            return new ReplicatedManagementKeys
            {
                HasKeys = false,
                Keys = [],
                RevokedKeyIds = revokedKeyIds,
            };
        }

        var now = TimeProvider.GetUtcNow();
        var keys = new List<SecretKey>();
        foreach (var entry in content.Keys)
        {
            if (revokedKeyIds.Contains(entry.KeyId))
                continue;

            if (entry.ExpiresWhen is { } expiresWhen && expiresWhen <= now)
                continue;

            var persistedSecret = ToPersistedSecret(entry);
            var key = SecretSerializer.DeserializePublicSecret(persistedSecret);
            keys.Add(key);
        }

        return new ReplicatedManagementKeys
        {
            HasKeys = true,
            Keys = keys,
            RevokedKeyIds = revokedKeyIds,
        };
    }

    /// <inheritdoc />
    public async ValueTask<ReplicatedAuthority> GetAuthorityAsync(
        string principalId,
        string? teamId,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(principalId);

        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<IReplicatedDocumentStore>();

        var platformDocument = await store.GetOrDefaultAsync(
            ReplicatedDocumentKinds.PlatformAuthority,
            string.Empty,
            cancellationToken
        );
        var platformContent = Deserialize<AuthorityDocumentContent>(platformDocument);

        var platformRoles = GetRoleNames(platformContent, principalId);
        var clientId = platformContent
            ?.Clients.FirstOrDefault(client =>
                string.Equals(client.PrincipalId, principalId, StringComparison.Ordinal)
            )
            ?.ClientId;

        var teamRoles = new List<string>();
        if (teamId is not null)
        {
            var teamIds = await GetTeamChainAsync(storeManager, teamId, cancellationToken);
            foreach (var chainTeamId in teamIds)
            {
                var teamDocument = await store.GetOrDefaultAsync(
                    ReplicatedDocumentKinds.TeamAuthority,
                    chainTeamId,
                    cancellationToken
                );
                var teamContent = Deserialize<AuthorityDocumentContent>(teamDocument);
                var chainRoles = GetRoleNames(teamContent, principalId);
                teamRoles.AddRange(chainRoles);
            }
        }

        return new ReplicatedAuthority
        {
            PlatformRoles = platformRoles,
            TeamRoles =
            [
                .. teamRoles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            ],
            ClientId = clientId,
        };
    }

    // The team and every team above it, lower-cased as the directory keys its team authority documents.
    private static async ValueTask<IReadOnlyList<string>> GetTeamChainAsync(
        IStoreManager storeManager,
        string teamId,
        CancellationToken cancellationToken
    )
    {
        var teamStore = storeManager.GetStore<ITeamStore>();
        var team = await teamStore.GetOrDefaultAsync(teamId, cancellationToken);

        IEnumerable<string> chain = team is null
            ? [teamId]
            : [.. team.AncestorTeamIds, team.TeamId];
        return [.. chain.Select(id => id.ToLowerInvariant()).Distinct(StringComparer.Ordinal)];
    }

    private static string[] GetRoleNames(AuthorityDocumentContent? content, string principalId)
    {
        if (content is null)
            return [];

        return
        [
            .. content
                .Assignments.Where(assignment =>
                    string.Equals(assignment.PrincipalId, principalId, StringComparison.Ordinal)
                )
                .Select(assignment => assignment.RoleName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static T? Deserialize<T>(PersistedReplicatedDocument? document)
        where T : class
    {
        if (document?.ContentJson is not { } contentJson)
            return null;

        return JsonSerializer.Deserialize<T>(contentJson, JsonOptions);
    }

    private static PersistedSecret ToPersistedSecret(ManagementKeyEntry entry)
    {
        var activatesWhen = entry.ActivatesWhen ?? DateTimeOffset.UnixEpoch;
        var expiresWhen = entry.ExpiresWhen ?? DateTimeOffset.MaxValue;
        var retiresWhen = entry.RetiresWhen ?? expiresWhen;

        return new PersistedSecret
        {
            SecretId = entry.KeyId,
            KeyId = entry.KeyId,
            Use = entry.Use,
            Algorithm = entry.Algorithm,
            CreatedWhen = activatesWhen,
            ActivatesWhen = activatesWhen,
            RetiresWhen = retiresWhen,
            ExpiresWhen = expiresWhen,
            SecretType = entry.SecretType,
            SecretEncoding = SecretEncodings.Spki,
            Origin = SecretOrigins.Supplied,
            KeySizeBits = entry.KeySizeBits ?? 0,
            EncodedValue = entry.PublicKey,
        };
    }
}
