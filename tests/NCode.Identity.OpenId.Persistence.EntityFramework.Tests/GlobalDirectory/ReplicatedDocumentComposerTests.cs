// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.GlobalDirectory;

public class ReplicatedDocumentComposerTests
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private static RoleAssignmentEntity CreateAssignment(string principalId, string roleName) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            NormalizedTenantId = "tenant",
            AssignmentId = Guid.NewGuid().ToString("N"),
            NormalizedAssignmentId = Guid.NewGuid().ToString("N"),
            PrincipalId = principalId,
            NormalizedPrincipalId = principalId.ToLowerInvariant(),
            RoleName = roleName,
            NormalizedRoleName = roleName.ToLowerInvariant(),
            ResourceType = "platform",
            ResourceId = "platform",
            NormalizedResourceId = "platform",
            ConcurrencyToken = "token",
            Tenant = null!,
        };

    private static ClientEntity CreateClient(
        string clientId,
        string principalId,
        bool isDisabled
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            NormalizedTenantId = "tenant",
            ClientId = clientId,
            NormalizedClientId = clientId.ToUpperInvariant(),
            PrincipalId = principalId,
            NormalizedPrincipalId = principalId.ToLowerInvariant(),
            OwningNodeType = null,
            OwningNodeId = null,
            NormalizedOwningNodeId = null,
            ConcurrencyToken = "token",
            SettingsConcurrencyToken = "token",
            SecretsConcurrencyToken = "token",
            IsDisabled = isDisabled,
            SettingsJson = JsonElement.Parse("{}"),
            ResourceServerId = null,
            Tenant = null!,
            ResourceServer = null,
            Secrets = [],
        };

    private static TenantSecretEntity CreateSecret(
        string keyId,
        string secretType,
        string? use = "sig",
        string? publicKey = "public-material",
        string encoding = "pkcs8",
        bool hasKeyId = true
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            NormalizedTenantId = "tenant",
            SecretId = Guid.NewGuid(),
            Tenant = null!,
            Secret = new SecretEntity
            {
                Id = Guid.NewGuid(),
                SecretId = keyId,
                NormalizedSecretId = keyId,
                KeyId = hasKeyId ? keyId : null,
                ConcurrencyToken = "token",
                Use = use,
                Algorithm = "ES256",
                CreatedWhen = DateTimeOffset.UnixEpoch,
                ActivatesWhen = DateTimeOffset.UnixEpoch,
                RetiresWhen = DateTimeOffset.UnixEpoch.AddYears(1),
                ExpiresWhen = DateTimeOffset.UnixEpoch.AddYears(2),
                SecretType = secretType,
                SecretEncoding = encoding,
                Origin = SecretOrigins.Generated,
                ManagedBy = null,
                KeySizeBits = 256,
                EncodedValue = "encoded-value",
                PublicKey = publicKey,
            },
        };

    private static RevokedManagementKeyEntity CreateRevoked(string keyId) =>
        new()
        {
            KeyId = keyId,
            RevokedWhen = DateTimeOffset.UnixEpoch,
            Source = "api",
        };

    [Fact]
    public void ComposeAuthority_WhenInputOrderDiffers_ProducesTheSameJson()
    {
        var first = ReplicatedDocumentComposer.ComposeAuthority(
            [
                CreateAssignment("b", "Admin"),
                CreateAssignment("a", "Viewer"),
                CreateAssignment("a", "Admin"),
            ],
            [CreateClient("two", "p2", false), CreateClient("one", "p1", false)]
        );
        var second = ReplicatedDocumentComposer.ComposeAuthority(
            [
                CreateAssignment("a", "Admin"),
                CreateAssignment("a", "Viewer"),
                CreateAssignment("b", "Admin"),
            ],
            [CreateClient("one", "p1", false), CreateClient("two", "p2", false)]
        );

        Assert.Equal(first, second);

        var content = JsonSerializer.Deserialize<AuthorityDocumentContent>(first, JsonOptions);
        Assert.NotNull(content);
        Assert.Equal(
            [("a", "Admin"), ("a", "Viewer"), ("b", "Admin")],
            content.Assignments.Select(item => (item.PrincipalId, item.RoleName))
        );
        Assert.Equal(["p1", "p2"], content.Clients.Select(item => item.PrincipalId));
    }

    [Fact]
    public void ComposeAuthority_WhenClientIsDisabled_LeavesItOut()
    {
        var json = ReplicatedDocumentComposer.ComposeAuthority(
            [],
            [CreateClient("on", "p1", false), CreateClient("off", "p2", true)]
        );

        var content = JsonSerializer.Deserialize<AuthorityDocumentContent>(json, JsonOptions);
        Assert.NotNull(content);
        var client = Assert.Single(content.Clients);
        Assert.Equal("on", client.ClientId);
    }

    [Fact]
    public void ComposeManagementKeys_WhenSecretIsSymmetric_LeavesItOut()
    {
        var json = ReplicatedDocumentComposer.ComposeManagementKeys(
            [CreateSecret("sym", SecretTypes.Symmetric), CreateSecret("ecc", SecretTypes.Ecc)],
            []
        );

        var content = JsonSerializer.Deserialize<ManagementKeysDocumentContent>(json, JsonOptions);
        Assert.NotNull(content);
        var key = Assert.Single(content.Keys);
        Assert.Equal("ecc", key.KeyId);
        Assert.Equal("public-material", key.PublicKey);
        Assert.Equal("sig", key.Use);
    }

    [Fact]
    public void ComposeManagementKeys_WhenSecretHoldsOnlyPublicMaterial_UsesTheEncodedValue()
    {
        var json = ReplicatedDocumentComposer.ComposeManagementKeys(
            [CreateSecret("pub", SecretTypes.Ecc, publicKey: null, encoding: SecretEncodings.Spki)],
            []
        );

        var content = JsonSerializer.Deserialize<ManagementKeysDocumentContent>(json, JsonOptions);
        Assert.NotNull(content);
        Assert.Equal("encoded-value", Assert.Single(content.Keys).PublicKey);
    }

    [Fact]
    public void ComposeManagementKeys_WhenSecretHasNoKeyId_UsesTheSecretId()
    {
        var secret = CreateSecret("generated-secret", SecretTypes.Ecc, hasKeyId: false);

        var json = ReplicatedDocumentComposer.ComposeManagementKeys([secret], []);

        var content = JsonSerializer.Deserialize<ManagementKeysDocumentContent>(json, JsonOptions);
        Assert.NotNull(content);
        Assert.Equal("generated-secret", Assert.Single(content.Keys).KeyId);
    }

    [Fact]
    public void ComposeManagementKeys_WhenUseIsNotSignature_LeavesItOut()
    {
        var json = ReplicatedDocumentComposer.ComposeManagementKeys(
            [
                CreateSecret("enc", SecretTypes.Rsa, use: "enc"),
                CreateSecret("any", SecretTypes.Rsa, use: null),
            ],
            []
        );

        var content = JsonSerializer.Deserialize<ManagementKeysDocumentContent>(json, JsonOptions);
        Assert.NotNull(content);
        Assert.Equal("any", Assert.Single(content.Keys).KeyId);
    }

    [Fact]
    public void ComposeManagementKeys_SortsKeysAndRevokedIds()
    {
        var json = ReplicatedDocumentComposer.ComposeManagementKeys(
            [CreateSecret("k2", SecretTypes.Ecc), CreateSecret("k1", SecretTypes.Ecc)],
            [CreateRevoked("z"), CreateRevoked("a")]
        );

        var content = JsonSerializer.Deserialize<ManagementKeysDocumentContent>(json, JsonOptions);
        Assert.NotNull(content);
        Assert.Equal(["k1", "k2"], content.Keys.Select(key => key.KeyId));
        Assert.Equal(["a", "z"], content.RevokedKeyIds);
    }
}
