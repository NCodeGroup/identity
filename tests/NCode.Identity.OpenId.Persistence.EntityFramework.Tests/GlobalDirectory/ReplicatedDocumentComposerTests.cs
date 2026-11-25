// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Json;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.GlobalDirectory;

public class ReplicatedDocumentComposerTests
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private static IPersistedPayloadSerializer PayloadSerializer { get; } =
        new ServiceCollection()
            .AddPersistedPayloadSerializer()
            .BuildServiceProvider()
            .GetRequiredService<IPersistedPayloadSerializer>();

    private static T Read<T>(string json) =>
        PayloadSerializer.Deserialize<T>(JsonElement.Parse(json), JsonOptions);

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
    public void ComposePlatformAuthority_WhenInputOrderDiffers_ProducesTheSameJson()
    {
        var first = ReplicatedDocumentComposer.ComposePlatformAuthority(
            PayloadSerializer,
            [
                CreateAssignment("b", "Admin"),
                CreateAssignment("a", "Viewer"),
                CreateAssignment("a", "Admin"),
            ],
            [CreateClient("two", "p2", false), CreateClient("one", "p1", false)]
        );
        var second = ReplicatedDocumentComposer.ComposePlatformAuthority(
            PayloadSerializer,
            [
                CreateAssignment("a", "Admin"),
                CreateAssignment("a", "Viewer"),
                CreateAssignment("b", "Admin"),
            ],
            [CreateClient("one", "p1", false), CreateClient("two", "p2", false)]
        );

        Assert.Equal(first, second);

        var content = Read<PlatformAuthorityDocumentContent>(first);
        Assert.Equal(
            [("a", "Admin"), ("a", "Viewer"), ("b", "Admin")],
            content.Assignments.Select(item => (item.PrincipalId, item.RoleName))
        );
        Assert.Equal(["p1", "p2"], content.Clients.Select(item => item.PrincipalId));
    }

    [Fact]
    public void ComposePlatformAuthority_WhenClientIsDisabled_LeavesItOut()
    {
        var json = ReplicatedDocumentComposer.ComposePlatformAuthority(
            PayloadSerializer,
            [],
            [CreateClient("on", "p1", false), CreateClient("off", "p2", true)]
        );

        var content = Read<PlatformAuthorityDocumentContent>(json);
        var client = Assert.Single(content.Clients);
        Assert.Equal("on", client.ClientId);
    }

    [Fact]
    public void ComposeManagementKeys_WhenSecretIsSymmetric_LeavesItOut()
    {
        var json = ReplicatedDocumentComposer.ComposeManagementKeys(
            PayloadSerializer,
            [CreateSecret("sym", SecretTypes.Symmetric), CreateSecret("ecc", SecretTypes.Ecc)],
            []
        );

        var content = Read<ManagementKeysDocumentContent>(json);
        var key = Assert.Single(content.Keys);
        Assert.Equal("ecc", key.KeyId);
        Assert.Equal("public-material", key.PublicKey);
        Assert.Equal("sig", key.Use);
    }

    [Fact]
    public void ComposeManagementKeys_WhenSecretHoldsOnlyPublicMaterial_UsesTheEncodedValue()
    {
        var json = ReplicatedDocumentComposer.ComposeManagementKeys(
            PayloadSerializer,
            [CreateSecret("pub", SecretTypes.Ecc, publicKey: null, encoding: SecretEncodings.Spki)],
            []
        );

        var content = Read<ManagementKeysDocumentContent>(json);
        Assert.Equal("encoded-value", Assert.Single(content.Keys).PublicKey);
    }

    [Fact]
    public void ComposeManagementKeys_WhenSecretHasNoKeyId_UsesTheSecretId()
    {
        var secret = CreateSecret("generated-secret", SecretTypes.Ecc, hasKeyId: false);

        var json = ReplicatedDocumentComposer.ComposeManagementKeys(
            PayloadSerializer,
            [secret],
            []
        );

        var content = Read<ManagementKeysDocumentContent>(json);
        Assert.Equal("generated-secret", Assert.Single(content.Keys).KeyId);
    }

    [Fact]
    public void ComposeManagementKeys_WhenUseIsNotSignature_LeavesItOut()
    {
        var json = ReplicatedDocumentComposer.ComposeManagementKeys(
            PayloadSerializer,
            [
                CreateSecret("enc", SecretTypes.Rsa, use: "enc"),
                CreateSecret("any", SecretTypes.Rsa, use: null),
            ],
            []
        );

        var content = Read<ManagementKeysDocumentContent>(json);
        Assert.Equal("any", Assert.Single(content.Keys).KeyId);
    }

    [Fact]
    public void ComposeManagementKeys_SortsKeysAndRevokedIds()
    {
        var json = ReplicatedDocumentComposer.ComposeManagementKeys(
            PayloadSerializer,
            [CreateSecret("k2", SecretTypes.Ecc), CreateSecret("k1", SecretTypes.Ecc)],
            [CreateRevoked("z"), CreateRevoked("a")]
        );

        var content = Read<ManagementKeysDocumentContent>(json);
        Assert.Equal(["k1", "k2"], content.Keys.Select(key => key.KeyId));
        Assert.Equal(["a", "z"], content.RevokedKeyIds);
    }

    [Fact]
    public void ComposePlatformAuthority_StoresTheContentInTheVersionedEnvelope()
    {
        var json = ReplicatedDocumentComposer.ComposePlatformAuthority(
            PayloadSerializer,
            [CreateAssignment("a", "Admin")],
            []
        );

        var root = JsonElement.Parse(json);
        Assert.Equal(1, root.GetProperty("$ver").GetInt32());
        var payload = root.GetProperty("$payload");
        Assert.Equal(1, payload.GetProperty("assignments").GetArrayLength());
        Assert.Equal(0, payload.GetProperty("clients").GetArrayLength());
    }

    [Fact]
    public void ComposeTeamAuthority_StoresTheAssignmentsWithoutClients()
    {
        var json = ReplicatedDocumentComposer.ComposeTeamAuthority(
            PayloadSerializer,
            [CreateAssignment("b", "Admin"), CreateAssignment("a", "Admin")]
        );

        var root = JsonElement.Parse(json);
        Assert.Equal(1, root.GetProperty("$ver").GetInt32());
        var payload = root.GetProperty("$payload");
        Assert.False(payload.TryGetProperty("clients", out _));
        Assert.Equal(
            ["a", "b"],
            payload
                .GetProperty("assignments")
                .EnumerateArray()
                .Select(item => item.GetProperty("principalId").GetString())
        );
    }

    [Fact]
    public void ComposeManagementKeys_StoresTheContentInTheVersionedEnvelope()
    {
        var json = ReplicatedDocumentComposer.ComposeManagementKeys(
            PayloadSerializer,
            [CreateSecret("k1", SecretTypes.Ecc)],
            [CreateRevoked("r1")]
        );

        var root = JsonElement.Parse(json);
        Assert.Equal(1, root.GetProperty("$ver").GetInt32());
        var payload = root.GetProperty("$payload");
        Assert.Equal(1, payload.GetProperty("keys").GetArrayLength());
        Assert.Equal("r1", payload.GetProperty("revokedKeyIds")[0].GetString());
    }
}
