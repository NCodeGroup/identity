// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.Entities;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;

/// <summary>
/// Composes the content of the replicated documents from persisted rows. Every function is pure and orders its output,
/// so the same rows always produce the same JSON, which is the versioned envelope of the content.
/// </summary>
internal static class ReplicatedDocumentComposer
{
    private const string SignatureUse = "sig";

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Composes the JSON content of an authority document.
    /// </summary>
    /// <param name="payloadSerializer">The serializer of the payloads stored at rest.</param>
    /// <param name="assignments">The role assignments at the node.</param>
    /// <param name="clients">The clients of the control-plane tenant, of which only the enabled ones are
    /// listed.</param>
    /// <returns>The JSON content.</returns>
    internal static string ComposeAuthority(
        IPersistedPayloadSerializer payloadSerializer,
        IEnumerable<RoleAssignmentEntity> assignments,
        IEnumerable<ClientEntity> clients
    )
    {
        var content = new AuthorityDocumentContent
        {
            Assignments = assignments
                .Select(assignment => new AuthorityAssignment
                {
                    PrincipalId = assignment.PrincipalId,
                    RoleName = assignment.RoleName,
                })
                .OrderBy(assignment => assignment.PrincipalId, StringComparer.Ordinal)
                .ThenBy(assignment => assignment.RoleName, StringComparer.Ordinal)
                .ToList(),
            Clients = clients
                .Where(client => !client.IsDisabled && !string.IsNullOrEmpty(client.PrincipalId))
                .Select(client => new AuthorityClient
                {
                    PrincipalId = client.PrincipalId,
                    ClientId = client.ClientId,
                })
                .OrderBy(client => client.PrincipalId, StringComparer.Ordinal)
                .ThenBy(client => client.ClientId, StringComparer.Ordinal)
                .ToList(),
        };

        return payloadSerializer.Serialize(content, JsonOptions).GetRawText();
    }

    /// <summary>
    /// Composes the JSON content of the management keys document.
    /// </summary>
    /// <param name="payloadSerializer">The serializer of the payloads stored at rest.</param>
    /// <param name="secrets">The secrets of the control-plane tenant.</param>
    /// <param name="revoked">The revoked management keys.</param>
    /// <returns>The JSON content.</returns>
    internal static string ComposeManagementKeys(
        IPersistedPayloadSerializer payloadSerializer,
        IEnumerable<TenantSecretEntity> secrets,
        IEnumerable<RevokedManagementKeyEntity> revoked
    )
    {
        var keys = new List<ManagementKeyEntry>();
        foreach (var tenantSecret in secrets)
        {
            var secret = tenantSecret.Secret;
            if (secret.SecretType is not (SecretTypes.Rsa or SecretTypes.Ecc))
                continue;

            if (
                secret.Use is not null
                && !string.Equals(secret.Use, SignatureUse, StringComparison.Ordinal)
            )
                continue;

            // A key without a key identifier is identified by its secret identifier, which its tokens' kid carries.
            var keyId = secret.KeyId ?? secret.SecretId;
            if (string.IsNullOrEmpty(keyId))
                continue;

            var publicKey = secret.PublicKey;
            if (string.IsNullOrEmpty(publicKey) && secret.SecretEncoding == SecretEncodings.Spki)
                publicKey = secret.EncodedValue;

            if (string.IsNullOrEmpty(publicKey))
                continue;

            var entry = new ManagementKeyEntry
            {
                KeyId = keyId,
                SecretType = secret.SecretType,
                Algorithm = secret.Algorithm ?? string.Empty,
                Use = SignatureUse,
                KeySizeBits = secret.KeySizeBits,
                PublicKey = publicKey,
                ActivatesWhen = secret.ActivatesWhen,
                RetiresWhen = secret.RetiresWhen,
                ExpiresWhen = secret.ExpiresWhen,
            };
            keys.Add(entry);
        }

        var content = new ManagementKeysDocumentContent
        {
            Keys = keys.OrderBy(key => key.KeyId, StringComparer.Ordinal).ToList(),
            RevokedKeyIds = revoked
                .Select(key => key.KeyId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(keyId => keyId, StringComparer.Ordinal)
                .ToList(),
        };

        return payloadSerializer.Serialize(content, JsonOptions).GetRawText();
    }
}
